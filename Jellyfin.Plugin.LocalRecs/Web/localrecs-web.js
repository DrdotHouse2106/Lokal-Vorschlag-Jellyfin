/*
 * Local Recommendations – Startseite im Netflix-Stil für Jellyfin Web (Browser und Jellyfin-Apps für iOS/Android).
 * Wird über das Plugin „File Transformation“ in index.html eingehängt. Ein- und ausschalten in den Plugin-Einstellungen.
 * Reihen wie in der TV-App Quallenkino: Weiterschauen, Als Nächstes, Empfehlungen mit Begründung, Heute im TV für dich,
 * Top 10, Welten, Jetzt im TV, Kürzlich hinzugefügt je Bibliothek, Genres und Bibliotheken.
 */
(function () {
    'use strict';

    if (window.__localRecsWeb) {
        return;
    }
    window.__localRecsWeb = true;

    var CACHE_VERSION = 2;
    var ROW_LIMIT = 24;
    var GENRE_ROWS = 6;
    var GENRE_MIN_ITEMS = 8;
    // Deutsche Genre-Namen (TMDB/Jellyfin auf Deutsch). Gibt es davon nichts, werden alle Genres genommen.
    var GERMAN_GENRES = ['Action', 'Abenteuer', 'Animation', 'Komödie', 'Krimi', 'Dokumentarfilm', 'Dokumentation', 'Drama',
        'Familie', 'Fantasy', 'Historie', 'Horror', 'Musik', 'Mystery', 'Liebesfilm', 'Romantik', 'Science Fiction',
        'Thriller', 'Kriegsfilm', 'Western', 'Action & Adventure', 'Sci-Fi & Fantasy', 'Krieg & Politik', 'Kinder', 'Zeichentrick',
        'Anime', 'Biografie', 'Sport', 'Musical', 'Erotik', 'Heimatfilm', 'Katastrophenfilm', 'Martial Arts', 'Satire', 'Superhelden'];
    var TRAILER_DELAY_MS = 2500;
    var FIELDS = 'Overview,Genres,PrimaryImageAspectRatio,ProductionYear,CommunityRating,OfficialRating,RunTimeTicks';
    var IMAGE_TYPES = 'Primary,Backdrop,Thumb,Logo';

    var isTouch = window.matchMedia('(hover: none), (pointer: coarse)').matches;
    var isPhone = /iPhone|iPad|iPod|Android/i.test(navigator.userAgent) || isTouch;
    var phoneQuery = window.matchMedia('(max-width: 700px)');
    var isNativeIos = !!window.NativeShell && /iPhone|iPad|iPod/i.test(navigator.userAgent);

    // Handy-Ansicht wie in der Netflix-App: Hochformat-Poster, Hero als Karte, Tab-Leiste unten
    function phoneLayout() {
        return phoneQuery.matches;
    }

    var state = {
        config: null,
        root: null,
        homeView: null,
        loading: false,
        lastLoad: 0,
        reasons: {},
        heroVideoTimer: null,
        previewTimer: null,
        preview: null
    };

    // ---------------------------------------------------------------- Hilfsfunktionen

    function api() {
        return window.ApiClient;
    }

    function userId() {
        var client = api();
        return client && client.getCurrentUserId ? client.getCurrentUserId() : null;
    }

    function url(path) {
        return api().getUrl(path.replace(/^\//, ''));
    }

    function getJson(path) {
        return api().getJSON(url(path));
    }

    function post(path) {
        return api().ajax({ type: 'POST', url: url(path) });
    }

    function del(path) {
        return api().ajax({ type: 'DELETE', url: url(path) });
    }

    function storageGet(key) {
        try {
            return window.localStorage.getItem(key);
        } catch (e) {
            return null;
        }
    }

    function storageSet(key, value) {
        try {
            if (value === null) {
                window.localStorage.removeItem(key);
            } else {
                window.localStorage.setItem(key, value);
            }
        } catch (e) {
            // Speicher voll oder gesperrt – dann eben ohne Zwischenspeicher
        }
    }

    function el(tag, className, text) {
        var node = document.createElement(tag);
        if (className) {
            node.className = className;
        }
        if (text !== undefined && text !== null) {
            node.textContent = text;
        }
        return node;
    }

    function icon(name) {
        var span = el('span', 'material-icons lr-icon');
        span.setAttribute('aria-hidden', 'true');
        span.textContent = name;
        return span;
    }

    function button(className, iconName, label, onClick) {
        var b = el('button', 'lr-btn ' + className);
        b.type = 'button';
        if (iconName) {
            b.appendChild(icon(iconName));
        }
        if (label) {
            b.appendChild(el('span', null, label));
        }
        b.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            onClick(e);
        });
        return b;
    }

    function imageUrl(id, type, tag, width) {
        var path = 'Items/' + id + '/Images/' + type + '?quality=85&maxWidth=' + (width || 640);
        return url(tag ? path + '&tag=' + tag : path);
    }

    function itemsQuery(base) {
        return base + (base.indexOf('?') < 0 ? '?' : '&') + 'userId=' + userId()
            + '&Fields=' + FIELDS + '&ImageTypeLimit=1&EnableImageTypes=' + IMAGE_TYPES;
    }

    function itemsOf(result) {
        if (!result) {
            return [];
        }
        return Array.isArray(result) ? result : (result.Items || []);
    }

    function runtimeText(ticks) {
        if (!ticks) {
            return '';
        }
        var minutes = Math.round(ticks / 600000000);
        return minutes >= 60 ? Math.floor(minutes / 60) + ' Std. ' + (minutes % 60) + ' Min.' : minutes + ' Min.';
    }

    function timeText(date) {
        var d = new Date(date);
        return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2);
    }

    // Nur die Felder speichern, die die Startseite braucht (für den Zwischenspeicher)
    function slim(item) {
        return {
            Id: item.Id,
            Name: item.Name,
            Type: item.Type,
            SeriesName: item.SeriesName,
            SeriesId: item.SeriesId,
            ParentIndexNumber: item.ParentIndexNumber,
            IndexNumber: item.IndexNumber,
            ProductionYear: item.ProductionYear,
            CommunityRating: item.CommunityRating,
            OfficialRating: item.OfficialRating,
            RunTimeTicks: item.RunTimeTicks,
            Genres: (item.Genres || []).slice(0, 3),
            Overview: item.Overview ? item.Overview.slice(0, 400) : null,
            ImageTags: item.ImageTags,
            BackdropImageTags: (item.BackdropImageTags || []).slice(0, 1),
            ParentBackdropItemId: item.ParentBackdropItemId,
            ParentBackdropImageTags: (item.ParentBackdropImageTags || []).slice(0, 1),
            ParentThumbItemId: item.ParentThumbItemId,
            ParentThumbImageTag: item.ParentThumbImageTag,
            ParentLogoItemId: item.ParentLogoItemId,
            ParentLogoImageTag: item.ParentLogoImageTag,
            SeriesPrimaryImageTag: item.SeriesPrimaryImageTag,
            ChannelId: item.ChannelId,
            ChannelName: item.ChannelName,
            ChannelPrimaryImageTag: item.ChannelPrimaryImageTag,
            StartDate: item.StartDate,
            EndDate: item.EndDate,
            EpisodeTitle: item.EpisodeTitle,
            CollectionType: item.CollectionType,
            UserData: item.UserData ? {
                PlayedPercentage: item.UserData.PlayedPercentage,
                PlaybackPositionTicks: item.UserData.PlaybackPositionTicks,
                IsFavorite: item.UserData.IsFavorite,
                Likes: item.UserData.Likes,
                Played: item.UserData.Played
            } : null
        };
    }

    // Querformat-Bild: Thumb, Backdrop, Serien-Bilder, sonst Primary
    function landscapeImage(item) {
        var tags = item.ImageTags || {};
        if (item.Type === 'Episode' && tags.Primary) {
            return imageUrl(item.Id, 'Primary', tags.Primary, 640);
        }
        if (tags.Thumb) {
            return imageUrl(item.Id, 'Thumb', tags.Thumb, 640);
        }
        if (item.BackdropImageTags && item.BackdropImageTags.length) {
            return imageUrl(item.Id, 'Backdrop', item.BackdropImageTags[0], 640);
        }
        if (item.ParentThumbItemId && item.ParentThumbImageTag) {
            return imageUrl(item.ParentThumbItemId, 'Thumb', item.ParentThumbImageTag, 640);
        }
        if (item.ParentBackdropItemId && item.ParentBackdropImageTags && item.ParentBackdropImageTags.length) {
            return imageUrl(item.ParentBackdropItemId, 'Backdrop', item.ParentBackdropImageTags[0], 640);
        }
        if (tags.Primary) {
            return imageUrl(item.Id, 'Primary', tags.Primary, 640);
        }
        if (item.ChannelId && item.ChannelPrimaryImageTag) {
            return imageUrl(item.ChannelId, 'Primary', item.ChannelPrimaryImageTag, 400);
        }
        return null;
    }

    function backdropImage(item, width) {
        if (item.BackdropImageTags && item.BackdropImageTags.length) {
            return imageUrl(item.Id, 'Backdrop', item.BackdropImageTags[0], width);
        }
        if (item.ParentBackdropItemId && item.ParentBackdropImageTags && item.ParentBackdropImageTags.length) {
            return imageUrl(item.ParentBackdropItemId, 'Backdrop', item.ParentBackdropImageTags[0], width);
        }
        return landscapeImage(item);
    }

    function logoImage(item, width) {
        var tags = item.ImageTags || {};
        if (tags.Logo) {
            return imageUrl(item.Id, 'Logo', tags.Logo, width || 500);
        }
        if (item.ParentLogoItemId && item.ParentLogoImageTag) {
            return imageUrl(item.ParentLogoItemId, 'Logo', item.ParentLogoImageTag, width || 500);
        }
        return null;
    }

    function posterImage(item) {
        var tags = item.ImageTags || {};
        if (item.Type === 'Episode' && item.SeriesId && item.SeriesPrimaryImageTag) {
            return imageUrl(item.SeriesId, 'Primary', item.SeriesPrimaryImageTag, 360);
        }
        if (tags.Primary) {
            return imageUrl(item.Id, 'Primary', tags.Primary, 360);
        }
        if (item.SeriesId && item.SeriesPrimaryImageTag) {
            return imageUrl(item.SeriesId, 'Primary', item.SeriesPrimaryImageTag, 360);
        }
        return null;
    }

    function displayTitle(item) {
        if (item.Type === 'Episode' && item.SeriesName) {
            return item.SeriesName;
        }
        return item.Name || '';
    }

    function subtitle(item, showReason) {
        if (item.Type === 'Episode') {
            var number = item.ParentIndexNumber != null && item.IndexNumber != null
                ? 'S' + item.ParentIndexNumber + ':F' + item.IndexNumber + ' ' : '';
            return number + (item.Name || '');
        }
        if (item.Type === 'Program' || item.Type === 'TvChannel') {
            var parts = [];
            if (item.StartDate) {
                parts.push(timeText(item.StartDate));
            }
            if (item.ChannelName) {
                parts.push(item.ChannelName);
            }
            if (item.EpisodeTitle) {
                parts.push(item.EpisodeTitle);
            }
            return parts.join(' · ');
        }
        return showReason ? state.reasons[item.Id] || '' : '';
    }

    function metaLine(item) {
        var parts = [];
        if (item.ProductionYear) {
            parts.push(String(item.ProductionYear));
        }
        if (item.OfficialRating) {
            parts.push(item.OfficialRating);
        }
        if (item.RunTimeTicks && item.Type === 'Movie') {
            parts.push(runtimeText(item.RunTimeTicks));
        }
        if (item.CommunityRating) {
            parts.push('★ ' + item.CommunityRating.toFixed(1));
        }
        return parts.join('  ·  ');
    }

    function isAiring(item) {
        var now = Date.now();
        return item.StartDate && item.EndDate && Date.parse(item.StartDate) <= now && Date.parse(item.EndDate) > now;
    }

    // ---------------------------------------------------------------- Navigation und Wiedergabe

    function navigate(route) {
        closeOverlays();
        window.location.hash = route;
    }

    function openDetailsPage(item) {
        navigate('/details?id=' + item.Id + '&serverId=' + api().serverId());
    }

    function openLibrary(view) {
        var type = view.CollectionType;
        var route;
        if (type === 'movies') {
            route = '/movies?topParentId=' + view.Id + '&collectionType=movies';
        } else if (type === 'tvshows') {
            route = '/tv?topParentId=' + view.Id + '&collectionType=tvshows';
        } else if (type === 'livetv') {
            route = '/livetv';
        } else if (type === 'music') {
            route = '/music?topParentId=' + view.Id + '&collectionType=music';
        } else {
            route = '/list?parentId=' + view.Id + '&serverId=' + api().serverId();
        }
        navigate(route);
    }

    // Eigene Sitzung (dieser Browser bzw. diese App) – Jellyfin Web nimmt „Abspielen“-Befehle an sich selbst an
    function ownSession() {
        var client = api();
        var deviceId = client.deviceId ? client.deviceId() : null;
        if (!deviceId) {
            return Promise.resolve(null);
        }
        return getJson('Sessions?deviceId=' + encodeURIComponent(deviceId)).then(function (sessions) {
            return (sessions || []).filter(function (s) {
                return s.SupportsRemoteControl !== false;
            })[0] || null;
        }).catch(function () {
            return null;
        });
    }

    function resolvePlayable(item) {
        if (item.Type === 'Series') {
            return getJson(itemsQuery('Shows/NextUp?seriesId=' + item.Id + '&limit=1')).then(function (result) {
                var next = itemsOf(result)[0];
                if (next) {
                    return next;
                }
                return getJson(itemsQuery('Shows/' + item.Id + '/Episodes?limit=1')).then(function (episodes) {
                    return itemsOf(episodes)[0] || null;
                });
            });
        }
        if (item.Type === 'Program') {
            return Promise.resolve(item.ChannelId ? { Id: item.ChannelId, Type: 'TvChannel' } : null);
        }
        return Promise.resolve(item);
    }

    function play(item) {
        resolvePlayable(item).then(function (target) {
            if (!target) {
                openDetailsPage(item);
                return null;
            }
            return ownSession().then(function (session) {
                if (!session) {
                    openDetailsPage(item);
                    return null;
                }
                var ticks = target.UserData && target.UserData.PlaybackPositionTicks ? target.UserData.PlaybackPositionTicks : 0;
                closeOverlays();
                stopHeroVideo();
                return post('Sessions/' + session.Id + '/Playing?playCommand=PlayNow&itemIds=' + target.Id
                    + (ticks ? '&startPositionTicks=' + ticks : ''));
            });
        }).catch(function () {
            openDetailsPage(item);
        });
    }

    // ---------------------------------------------------------------- Karten und Reihen

    function onActivate(node, handler) {
        node.tabIndex = 0;
        node.addEventListener('click', handler);
        node.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                handler(e);
            }
        });
    }

    function itemClick(item) {
        if (item.Type === 'Program' || item.Type === 'TvChannel') {
            if (item.Type === 'TvChannel' || isAiring(item)) {
                play(item);
            } else {
                openDetailsPage(item);
            }
            return;
        }
        openInfo(item);
    }

    // Bild für Querformat-Kacheln. "clean" = ohne eingebrannten Titel (Backdrop, Folgenbild), dann kommt Logo/Titel darüber.
    // Thumb-Bilder enthalten meist schon den Titel – die bekommen kein zweites Logo.
    function cardImage(item) {
        var tags = item.ImageTags || {};
        var backdrop = item.BackdropImageTags && item.BackdropImageTags.length;
        var parentBackdrop = item.ParentBackdropItemId && item.ParentBackdropImageTags && item.ParentBackdropImageTags.length;
        if (item.Type === 'Episode' && tags.Primary) {
            return { src: imageUrl(item.Id, 'Primary', tags.Primary, 640), clean: true };
        }
        if (item.Type === 'Program' || item.Type === 'TvChannel') {
            var programImage = landscapeImage(item);
            return { src: programImage, clean: true };
        }
        if (backdrop) {
            return { src: imageUrl(item.Id, 'Backdrop', item.BackdropImageTags[0], 640), clean: true };
        }
        if (parentBackdrop) {
            return { src: imageUrl(item.ParentBackdropItemId, 'Backdrop', item.ParentBackdropImageTags[0], 640), clean: true };
        }
        if (tags.Thumb) {
            return { src: imageUrl(item.Id, 'Thumb', tags.Thumb, 640), clean: false };
        }
        if (item.ParentThumbItemId && item.ParentThumbImageTag) {
            return { src: imageUrl(item.ParentThumbItemId, 'Thumb', item.ParentThumbImageTag, 640), clean: false };
        }
        if (tags.Primary) {
            return { src: imageUrl(item.Id, 'Primary', tags.Primary, 640), clean: true };
        }
        if (item.SeriesId && item.SeriesPrimaryImageTag) {
            return { src: imageUrl(item.SeriesId, 'Primary', item.SeriesPrimaryImageTag, 640), clean: true };
        }
        return { src: null, clean: true };
    }

    function landscapeCard(item, showReason) {
        var card = el('div', 'lr-card lr-card-landscape');
        var image = cardImage(item);
        var frame = el('div', 'lr-card-frame');
        if (image.src) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = '';
            img.src = image.src;
            img.addEventListener('error', function () {
                img.remove();
                frame.classList.add('lr-card-noimg');
            });
            frame.appendChild(img);
        } else {
            frame.classList.add('lr-card-noimg');
        }
        var logo = image.clean && item.Type !== 'Episode' && item.Type !== 'Program' ? logoImage(item, 300) : null;
        if (!image.clean) {
            // Titel steht schon im Bild
        } else if (logo) {
            var logoImg = el('img', 'lr-card-logo');
            logoImg.loading = 'lazy';
            logoImg.alt = displayTitle(item);
            logoImg.src = logo;
            frame.appendChild(logoImg);
        } else {
            frame.appendChild(el('div', 'lr-card-title', displayTitle(item)));
        }
        var percent = item.UserData && item.UserData.PlayedPercentage;
        if (percent && percent > 0 && percent < 100) {
            var bar = el('div', 'lr-progress');
            var fill = el('div', 'lr-progress-fill');
            fill.style.width = percent + '%';
            bar.appendChild(fill);
            frame.appendChild(bar);
        }
        if (item.Type === 'Program' && isAiring(item)) {
            frame.appendChild(el('div', 'lr-live', 'LIVE'));
        }
        card.appendChild(frame);
        var sub = subtitle(item, showReason);
        if (sub) {
            card.appendChild(el('div', 'lr-card-sub', sub));
        }
        onActivate(card, function () {
            itemClick(item);
        });
        attachPreview(card, item);
        return card;
    }

    function portraitCard(item) {
        var card = el('div', 'lr-card lr-card-poster lr-card-portrait');
        var frame = el('div', 'lr-card-frame lr-poster');
        var poster = posterImage(item);
        if (poster) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = displayTitle(item);
            img.src = poster;
            frame.appendChild(img);
        } else {
            frame.appendChild(el('div', 'lr-card-title', displayTitle(item)));
        }
        var percent = item.UserData && item.UserData.PlayedPercentage;
        if (percent && percent > 0 && percent < 100) {
            var bar = el('div', 'lr-progress');
            var fill = el('div', 'lr-progress-fill');
            fill.style.width = percent + '%';
            bar.appendChild(fill);
            frame.appendChild(bar);
            frame.appendChild(el('span', 'material-icons lr-card-play', 'play_circle_outline'));
        }
        card.appendChild(frame);
        if (item.Type === 'Episode') {
            card.appendChild(el('div', 'lr-card-sub', subtitle(item)));
        }
        onActivate(card, function () {
            if (percent && percent > 0 && percent < 100) {
                play(item);
            } else {
                itemClick(item);
            }
        });
        return card;
    }

    function top10Card(item, rank) {
        var card = el('div', 'lr-card lr-card-top10');
        var frame = el('div', 'lr-card-frame lr-poster');
        var poster = posterImage(item);
        if (poster) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = displayTitle(item);
            img.src = poster;
            frame.appendChild(img);
        } else {
            frame.appendChild(el('div', 'lr-card-title', displayTitle(item)));
        }
        card.appendChild(frame);
        card.appendChild(el('div', 'lr-rank', String(rank)));
        onActivate(card, function () {
            openInfo(item);
        });
        attachPreview(card, item);
        return card;
    }

    function posterCard(item) {
        var card = el('div', 'lr-card lr-card-poster');
        var frame = el('div', 'lr-card-frame lr-poster');
        var poster = posterImage(item);
        if (poster) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = displayTitle(item);
            img.src = poster;
            frame.appendChild(img);
        } else {
            frame.appendChild(el('div', 'lr-card-title', displayTitle(item)));
        }
        card.appendChild(frame);
        onActivate(card, function () {
            openInfo(item);
        });
        return card;
    }

    function worldCard(world) {
        var card = el('div', 'lr-card lr-card-landscape lr-card-world');
        var frame = el('div', 'lr-card-frame');
        if (world.ImageItemId) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = '';
            img.src = imageUrl(world.ImageItemId, 'Backdrop', null, 640);
            frame.appendChild(img);
        } else {
            frame.classList.add('lr-card-noimg');
        }
        if (world.LogoUrl) {
            var logo = el('img', 'lr-world-logo');
            logo.loading = 'lazy';
            logo.alt = world.Name;
            logo.src = url(world.LogoUrl);
            logo.addEventListener('error', function () {
                logo.replaceWith(el('div', 'lr-world-name', world.Name));
            });
            frame.appendChild(logo);
        } else {
            frame.appendChild(el('div', 'lr-world-name', world.Name));
        }
        card.appendChild(frame);
        card.appendChild(el('div', 'lr-card-sub', world.ItemCount + ' Titel'));
        onActivate(card, function () {
            openWorld(world);
        });
        return card;
    }

    function libraryCard(view) {
        var card = el('div', 'lr-card lr-card-landscape lr-card-library');
        var frame = el('div', 'lr-card-frame');
        var tag = view.ImageTags && view.ImageTags.Primary;
        if (tag) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = '';
            img.src = imageUrl(view.Id, 'Primary', tag, 640);
            frame.appendChild(img);
        } else {
            frame.classList.add('lr-card-noimg');
        }
        frame.appendChild(el('div', 'lr-card-title lr-library-title', view.Name));
        card.appendChild(frame);
        onActivate(card, function () {
            openLibrary(view);
        });
        return card;
    }

    function rowElement(title, cards, extraClass) {
        var row = el('section', 'lr-row' + (extraClass ? ' ' + extraClass : ''));
        row.appendChild(el('h2', 'lr-row-title', title));
        var wrap = el('div', 'lr-row-wrap');
        var track = el('div', 'lr-track');
        cards.forEach(function (c) {
            track.appendChild(c);
        });
        wrap.appendChild(track);
        if (!isTouch) {
            var left = button('lr-arrow lr-arrow-left', 'chevron_left', null, function () {
                track.scrollBy({ left: -track.clientWidth * 0.9, behavior: 'smooth' });
            });
            var right = button('lr-arrow lr-arrow-right', 'chevron_right', null, function () {
                track.scrollBy({ left: track.clientWidth * 0.9, behavior: 'smooth' });
            });
            left.setAttribute('aria-label', 'Zurück');
            right.setAttribute('aria-label', 'Weiter');
            wrap.appendChild(left);
            wrap.appendChild(right);
            var update = function () {
                left.classList.toggle('lr-hidden', track.scrollLeft < 10);
                right.classList.toggle('lr-hidden', track.scrollLeft + track.clientWidth >= track.scrollWidth - 10);
            };
            track.addEventListener('scroll', update, { passive: true });
            window.setTimeout(update, 50);
        }
        row.appendChild(wrap);
        return row;
    }

    // ---------------------------------------------------------------- Vorschau beim Überfahren (nur PC)

    function attachPreview(card, item) {
        if (isTouch) {
            return;
        }
        card.addEventListener('mouseenter', function () {
            window.clearTimeout(state.previewTimer);
            state.previewTimer = window.setTimeout(function () {
                showPreview(card, item);
            }, 500);
        });
        card.addEventListener('mouseleave', function () {
            window.clearTimeout(state.previewTimer);
        });
    }

    function hidePreview() {
        window.clearTimeout(state.previewTimer);
        if (state.preview) {
            state.preview.remove();
            state.preview = null;
        }
    }

    function showPreview(card, item) {
        hidePreview();
        if (!document.body.contains(card)) {
            return;
        }
        var rect = card.getBoundingClientRect();
        var width = Math.max(rect.width * 1.45, 300);
        var left = Math.min(Math.max(8, rect.left + rect.width / 2 - width / 2), window.innerWidth - width - 8);
        var popup = el('div', 'lr-preview');
        popup.style.width = width + 'px';
        popup.style.left = left + 'px';
        popup.style.top = Math.max(8, rect.top + window.scrollY - rect.height * 0.25) + 'px';
        var image = backdropImage(item, 780);
        var frame = el('div', 'lr-preview-frame');
        if (image) {
            var img = el('img', 'lr-card-img');
            img.alt = '';
            img.src = image;
            frame.appendChild(img);
        }
        var logo = logoImage(item, 300);
        if (logo) {
            var logoImg = el('img', 'lr-card-logo');
            logoImg.alt = displayTitle(item);
            logoImg.src = logo;
            frame.appendChild(logoImg);
        } else {
            frame.appendChild(el('div', 'lr-card-title', displayTitle(item)));
        }
        popup.appendChild(frame);
        var body = el('div', 'lr-preview-body');
        var actions = el('div', 'lr-preview-actions');
        actions.appendChild(button('lr-round lr-round-white', 'play_arrow', null, function () {
            hidePreview();
            play(item);
        }));
        actions.appendChild(favoriteButton(item));
        actions.appendChild(likeButton(item));
        var more = button('lr-round lr-round-right', 'expand_more', null, function () {
            hidePreview();
            itemClick(item);
        });
        more.title = 'Weitere Infos';
        actions.appendChild(more);
        body.appendChild(actions);
        var meta = metaLine(item);
        if (meta) {
            body.appendChild(el('div', 'lr-preview-meta', meta));
        }
        if (item.Type === 'Episode') {
            body.appendChild(el('div', 'lr-preview-meta', subtitle(item)));
        }
        if (item.Genres && item.Genres.length) {
            body.appendChild(el('div', 'lr-preview-genres', item.Genres.slice(0, 3).join(' • ')));
        }
        if (state.reasons[item.Id]) {
            body.appendChild(el('div', 'lr-preview-reason', state.reasons[item.Id]));
        }
        popup.appendChild(body);
        popup.addEventListener('mouseleave', hidePreview);
        popup.addEventListener('click', function (e) {
            if (e.target.closest('button')) {
                return;
            }
            hidePreview();
            itemClick(item);
        });
        document.body.appendChild(popup);
        state.preview = popup;
        window.requestAnimationFrame(function () {
            popup.classList.add('lr-preview-open');
        });
    }

    function favoriteButton(item) {
        var data = item.UserData || (item.UserData = {});
        var b = button('lr-round', data.IsFavorite ? 'check' : 'add', null, function () {
            var on = !data.IsFavorite;
            (on ? post('UserFavoriteItems/' + item.Id + '?userId=' + userId()) : del('UserFavoriteItems/' + item.Id + '?userId=' + userId()))
                .then(function () {
                    data.IsFavorite = on;
                    b.querySelector('.lr-icon').textContent = on ? 'check' : 'add';
                });
        });
        b.title = 'Meine Liste (Favorit)';
        return b;
    }

    function likeButton(item) {
        var data = item.UserData || (item.UserData = {});
        var b = button('lr-round', data.Likes === true ? 'thumb_up' : 'thumb_up_off_alt', null, function () {
            var request = data.Likes === true
                ? del('UserItems/' + item.Id + '/Rating?userId=' + userId())
                : post('UserItems/' + item.Id + '/Rating?userId=' + userId() + '&likes=true');
            request.then(function () {
                data.Likes = data.Likes === true ? null : true;
                b.querySelector('.lr-icon').textContent = data.Likes === true ? 'thumb_up' : 'thumb_up_off_alt';
            });
        });
        b.title = 'Gefällt mir';
        return b;
    }

    // ---------------------------------------------------------------- Infobereich (Hero) mit Trailer

    function stopHeroVideo() {
        window.clearTimeout(state.heroVideoTimer);
        document.querySelectorAll('.lr-hero-video').forEach(function (video) {
            try {
                video.pause();
            } catch (e) {
                // egal
            }
            video.remove();
        });
        document.querySelectorAll('.lr-hero.lr-playing').forEach(function (hero) {
            hero.classList.remove('lr-playing');
        });
        document.querySelectorAll('.lr-mute').forEach(function (mute) {
            mute.classList.add('lr-hidden');
        });
    }

    function startTrailer(hero, trailerId) {
        window.clearTimeout(state.heroVideoTimer);
        state.heroVideoTimer = window.setTimeout(function () {
            if (!document.body.contains(hero) || hero.offsetParent === null) {
                return;
            }
            var video = el('video', 'lr-hero-video');
            // Immer stumm starten, auf Handys bleibt der Ton aus
            video.muted = true;
            video.defaultMuted = true;
            video.setAttribute('muted', '');
            video.playsInline = true;
            video.setAttribute('playsinline', '');
            video.setAttribute('webkit-playsinline', '');
            video.autoplay = true;
            video.preload = 'auto';
            video.src = url('Videos/' + trailerId + '/stream?static=true&api_key=' + api().accessToken());
            video.addEventListener('playing', function () {
                hero.classList.add('lr-playing');
                var muteButton = hero.querySelector('.lr-mute');
                if (muteButton) {
                    muteButton.classList.remove('lr-hidden');
                    muteButton.querySelector('.lr-icon').textContent = video.muted ? 'volume_off' : 'volume_up';
                }
            });
            video.addEventListener('ended', stopHeroVideo);
            video.addEventListener('error', stopHeroVideo);
            hero.querySelector('.lr-hero-media').appendChild(video);
            var promise = video.play();
            if (promise && promise.catch) {
                promise.catch(stopHeroVideo);
            }
        }, TRAILER_DELAY_MS);
    }

    function loadTrailer(hero, item) {
        if (item.TrailerId) {
            startTrailer(hero, item.TrailerId);
            return;
        }
        if (item.Type !== 'Movie' && item.Type !== 'Series') {
            return;
        }
        getJson('Items/' + item.Id + '/LocalTrailers?userId=' + userId()).then(function (trailers) {
            var first = itemsOf(trailers)[0];
            if (first) {
                startTrailer(hero, first.Id);
            }
        }).catch(function () {
            // ohne Trailer bleibt das Bild
        });
    }

    function heroElement(item, options) {
        options = options || {};
        var card = !!options.card && phoneLayout();
        var hero = el('div', 'lr-hero' + (options.compact ? ' lr-hero-compact' : '') + (card ? ' lr-hero-card' : ''));
        var media = el('div', 'lr-hero-media');
        var image = options.image || (card ? posterImage(item) || backdropImage(item, 1280) : backdropImage(item, 1920));
        if (card && posterImage(item)) {
            image = image.replace('maxWidth=360', 'maxWidth=900');
        }
        if (image) {
            var img = el('img', 'lr-hero-img');
            img.alt = '';
            img.src = image;
            media.appendChild(img);
        }
        hero.appendChild(media);
        hero.appendChild(el('div', 'lr-hero-shade'));
        var content = el('div', 'lr-hero-content');
        var logo = options.logo || logoImage(item, 800);
        if (logo) {
            var logoImg = el('img', 'lr-hero-logo');
            logoImg.alt = options.title || displayTitle(item);
            logoImg.src = logo;
            logoImg.addEventListener('error', function () {
                logoImg.replaceWith(el('h1', 'lr-hero-title', options.title || displayTitle(item)));
            });
            content.appendChild(logoImg);
        } else {
            content.appendChild(el('h1', 'lr-hero-title', options.title || displayTitle(item)));
        }
        if (options.badge) {
            content.appendChild(el('div', 'lr-hero-badge', options.badge));
        }
        var meta = options.meta !== undefined ? options.meta
            : (card && item.Genres && item.Genres.length ? item.Genres.slice(0, 3).join(' • ') : metaLine(item));
        if (meta) {
            content.appendChild(el('div', 'lr-hero-meta', meta));
        }
        var overview = options.overview !== undefined ? options.overview : item.Overview;
        if (overview) {
            content.appendChild(el('p', 'lr-hero-overview', overview));
        }
        if (options.actions !== false) {
            var actions = el('div', 'lr-hero-actions');
            var resume = item.UserData && item.UserData.PlaybackPositionTicks > 0;
            actions.appendChild(button('lr-btn-play', 'play_arrow', resume ? 'Fortsetzen' : 'Abspielen', function () {
                play(item);
            }));
            if (card) {
                var listButton = button('lr-btn-info', item.UserData && item.UserData.IsFavorite ? 'check' : 'add', 'Meine Liste', function () {
                    var data = item.UserData || (item.UserData = {});
                    var on = !data.IsFavorite;
                    (on ? post('UserFavoriteItems/' + item.Id + '?userId=' + userId()) : del('UserFavoriteItems/' + item.Id + '?userId=' + userId()))
                        .then(function () {
                            data.IsFavorite = on;
                            listButton.querySelector('.lr-icon').textContent = on ? 'check' : 'add';
                        });
                });
                actions.appendChild(listButton);
                var infoButton = button('lr-btn-icon', 'info_outline', 'Info', function () {
                    openInfo(item);
                });
                actions.appendChild(infoButton);
                media.addEventListener('click', function () {
                    openInfo(item);
                });
            } else if (!options.noInfo) {
                actions.appendChild(button('lr-btn-info', 'info_outline', 'Weitere Infos', function () {
                    openInfo(item);
                }));
            }
            content.appendChild(actions);
        }
        hero.appendChild(content);
        if (!isPhone) {
            var mute = button('lr-round lr-mute lr-hidden', 'volume_off', null, function () {
                var video = hero.querySelector('.lr-hero-video');
                if (video) {
                    video.muted = !video.muted;
                    mute.querySelector('.lr-icon').textContent = video.muted ? 'volume_off' : 'volume_up';
                }
            });
            mute.title = 'Ton';
            hero.appendChild(mute);
        }
        if (options.trailerId) {
            startTrailer(hero, options.trailerId);
        } else if (options.trailer !== false) {
            loadTrailer(hero, item);
        }
        return hero;
    }

    // ---------------------------------------------------------------- Detail-Overlay („Weitere Infos“)

    var overlayStack = [];

    function closeOverlays() {
        hidePreview();
        while (overlayStack.length) {
            overlayStack.pop().remove();
        }
        document.documentElement.classList.remove('lr-noscroll');
        stopHeroVideo();
    }

    function closeTopOverlay() {
        var top = overlayStack.pop();
        if (top) {
            top.querySelectorAll('video').forEach(function (v) {
                v.pause();
            });
            top.remove();
        }
        if (!overlayStack.length) {
            document.documentElement.classList.remove('lr-noscroll');
        }
    }

    function overlayShell(className) {
        hidePreview();
        stopHeroVideo();
        var overlay = el('div', 'lr-overlay ' + (className || ''));
        var dialog = el('div', 'lr-dialog');
        var close = button('lr-round lr-close', 'close', null, closeTopOverlay);
        close.title = 'Schließen';
        dialog.appendChild(close);
        overlay.appendChild(dialog);
        overlay.addEventListener('click', function (e) {
            if (e.target === overlay) {
                closeTopOverlay();
            }
        });
        document.body.appendChild(overlay);
        overlayStack.push(overlay);
        document.documentElement.classList.add('lr-noscroll');
        return dialog;
    }

    function openInfo(summary) {
        var dialog = overlayShell('lr-info');
        dialog.appendChild(el('div', 'lr-spinner'));
        getJson(itemsQuery('Items/' + summary.Id).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines'))
            .then(function (item) {
                dialog.querySelector('.lr-spinner').remove();
                renderInfo(dialog, item);
            })
            .catch(function () {
                dialog.querySelector('.lr-spinner').remove();
                renderInfo(dialog, summary);
            });
    }

    function renderInfo(dialog, item, onNative, options) {
        options = options || {};
        var reason = state.reasons[item.Id];
        var isEpisode = item.Type === 'Episode';
        dialog.appendChild(heroElement(item, { compact: true, noInfo: true, overview: null, meta: '', badge: reason }));
        var body = el('div', 'lr-info-body');
        var left = el('div', 'lr-info-main');
        if (isEpisode) {
            left.appendChild(el('h2', 'lr-info-episode', subtitle(item)));
        }
        var meta = metaLine(item);
        if (meta) {
            left.appendChild(el('div', 'lr-info-meta', meta));
        }
        var actions = el('div', 'lr-info-actions');
        actions.appendChild(favoriteButton(item));
        actions.appendChild(likeButton(item));
        if (onNative) {
            actions.appendChild(el('span', 'lr-native-actions'));
        }
        if (isEpisode && item.SeriesId) {
            actions.appendChild(button('lr-btn-link', 'tv', 'Zur Serie', function () {
                openDetailsPage({ Id: item.SeriesId });
            }));
        }
        // Jellyfin-Detailseite mit allen Optionen (Versionen, Tonspur, Untertitel, Bearbeiten …)
        actions.appendChild(button('lr-btn-link', 'tune', 'Alle Optionen', function () {
            if (onNative) {
                onNative();
                return;
            }
            try {
                window.sessionStorage.setItem('lrWeb.nativeDetail', item.Id);
            } catch (e) {
                // egal
            }
            openDetailsPage(item);
        }));
        left.appendChild(actions);
        if (item.Taglines && item.Taglines.length) {
            left.appendChild(el('div', 'lr-info-tagline', item.Taglines[0]));
        }
        if (item.Overview) {
            left.appendChild(el('p', 'lr-info-overview', item.Overview));
        }
        body.appendChild(left);
        var side = el('div', 'lr-info-side');
        var cast = (item.People || []).filter(function (p) {
            return p.Type === 'Actor';
        }).slice(0, 6).map(function (p) {
            return p.Name;
        });
        if (cast.length) {
            side.appendChild(sideLine('Besetzung', cast.join(', ')));
        }
        var directors = (item.People || []).filter(function (p) {
            return p.Type === 'Director';
        }).slice(0, 2).map(function (p) {
            return p.Name;
        });
        if (directors.length) {
            side.appendChild(sideLine('Regie', directors.join(', ')));
        }
        if (item.Genres && item.Genres.length) {
            side.appendChild(sideLine('Genres', item.Genres.join(', ')));
        }
        if (item.Studios && item.Studios.length) {
            side.appendChild(sideLine('Studio', item.Studios.slice(0, 2).map(function (s) {
                return s.Name;
            }).join(', ')));
        }
        body.appendChild(side);
        dialog.appendChild(body);

        if (item.Type === 'Series') {
            renderSeasons(dialog, item, options.seasonId);
        } else if (isEpisode && item.SeriesId) {
            renderSeasons(dialog, { Id: item.SeriesId }, item.SeasonId, item.Id);
        } else if (item.Type === 'Movie') {
            getJson(itemsQuery('Items/' + item.Id + '/Similar?limit=12')).then(function (result) {
                var similar = itemsOf(result);
                if (similar.length) {
                    var section = el('div', 'lr-info-section');
                    section.appendChild(el('h3', 'lr-info-heading', 'Ähnliche Titel'));
                    var grid = el('div', 'lr-grid');
                    similar.forEach(function (s) {
                        grid.appendChild(posterCard(s));
                    });
                    section.appendChild(grid);
                    dialog.appendChild(section);
                }
            }).catch(function () {
                // ohne ähnliche Titel
            });
        }
    }

    function sideLine(label, text) {
        var line = el('div', 'lr-info-line');
        line.appendChild(el('span', 'lr-info-label', label + ': '));
        line.appendChild(el('span', null, text));
        return line;
    }

    function renderSeasons(dialog, series, preferSeasonId, currentEpisodeId) {
        getJson(itemsQuery('Shows/' + series.Id + '/Seasons')).then(function (result) {
            var seasons = itemsOf(result);
            if (!seasons.length) {
                return;
            }
            var section = el('div', 'lr-info-section');
            var head = el('div', 'lr-season-head');
            head.appendChild(el('h3', 'lr-info-heading', 'Folgen'));
            var select = el('select', 'lr-select');
            seasons.forEach(function (s) {
                var option = el('option', null, s.Name);
                option.value = s.Id;
                select.appendChild(option);
            });
            head.appendChild(select);
            section.appendChild(head);
            var list = el('div', 'lr-episodes');
            section.appendChild(list);
            dialog.appendChild(section);
            var load = function () {
                list.innerHTML = '';
                list.appendChild(el('div', 'lr-spinner'));
                getJson(itemsQuery('Shows/' + series.Id + '/Episodes?seasonId=' + select.value)).then(function (episodes) {
                    list.innerHTML = '';
                    itemsOf(episodes).forEach(function (episode) {
                        var row = episodeRow(episode);
                        if (episode.Id === currentEpisodeId) {
                            row.classList.add('lr-current');
                            window.setTimeout(function () {
                                row.scrollIntoView({ block: 'nearest' });
                            }, 50);
                        }
                        list.appendChild(row);
                    });
                });
            };
            select.addEventListener('change', load);
            if (preferSeasonId && seasons.some(function (s) {
                return s.Id === preferSeasonId;
            })) {
                select.value = preferSeasonId;
                load();
                return;
            }
            // Staffel mit dem nächsten ungesehenen Teil vorauswählen
            getJson(itemsQuery('Shows/NextUp?seriesId=' + series.Id + '&limit=1')).then(function (next) {
                var episode = itemsOf(next)[0];
                if (episode && episode.SeasonId) {
                    select.value = episode.SeasonId;
                }
            }).catch(function () {
                // erste Staffel
            }).then(load);
        });
    }

    function episodeRow(episode) {
        var row = el('div', 'lr-episode');
        row.appendChild(el('div', 'lr-episode-number', episode.IndexNumber != null ? String(episode.IndexNumber) : ''));
        var frame = el('div', 'lr-episode-frame');
        var image = landscapeImage(episode);
        if (image) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = '';
            img.src = image;
            frame.appendChild(img);
        }
        var percent = episode.UserData && episode.UserData.PlayedPercentage;
        if (percent && percent > 0 && percent < 100) {
            var bar = el('div', 'lr-progress');
            var fill = el('div', 'lr-progress-fill');
            fill.style.width = percent + '%';
            bar.appendChild(fill);
            frame.appendChild(bar);
        }
        frame.appendChild(el('span', 'material-icons lr-episode-play', 'play_circle_outline'));
        row.appendChild(frame);
        var text = el('div', 'lr-episode-text');
        var title = el('div', 'lr-episode-title');
        title.appendChild(el('span', null, episode.Name || ''));
        title.appendChild(el('span', 'lr-episode-runtime', runtimeText(episode.RunTimeTicks)));
        text.appendChild(title);
        if (episode.Overview) {
            text.appendChild(el('div', 'lr-episode-overview', episode.Overview));
        }
        if (episode.UserData && episode.UserData.Played) {
            row.classList.add('lr-watched');
        }
        row.appendChild(text);
        onActivate(row, function () {
            play(episode);
        });
        return row;
    }

    // ---------------------------------------------------------------- Detailseite im Netflix-Stil

    var DETAIL_TYPES = ['Movie', 'Series', 'Episode', 'Season'];

    function detailIdFromHash() {
        var match = /[?&]id=([0-9a-fA-F-]{32,36})/.exec(window.location.hash || '');
        return match ? match[1].replace(/-/g, '') : null;
    }

    function showNativeDetail(view) {
        view.classList.remove('lr-detail-active');
        var page = view.querySelector(':scope > .lr-detail-page');
        if (page) {
            page.remove();
        }
        stopHeroVideo();
    }

    // Knöpfe der Jellyfin-Detailseite (Gesehen, Trailer, Download, ⋮ mit Löschen, Bearbeiten …) im neuen Design anbieten.
    // Ein Klick löst den Original-Knopf aus – Rechte, Rückfragen und Menüs bleiben die von Jellyfin.
    var MIRRORED_BUTTONS = ['btnReplay', 'btnPlayTrailer', 'btnShuffle', 'btnInstantMix', 'btnDownload', 'btnPlaystate',
        'btnSplitVersions', 'btnCancelSeriesTimer', 'btnCancelTimer', 'btnMoreCommands'];

    function clickNative(native, anchor) {
        // Menüs (z. B. ⋮) öffnen sich am Knopf – den versteckten Original-Knopf kurz an unsere Stelle legen
        var rect = anchor.getBoundingClientRect();
        var previous = native.getAttribute('style') || '';
        native.setAttribute('style', previous + ';visibility:visible!important;position:fixed!important;left:' + rect.left + 'px!important;top:'
            + rect.top + 'px!important;width:' + rect.width + 'px!important;height:' + rect.height + 'px!important;opacity:0!important;pointer-events:none!important;z-index:-1!important');
        native.click();
        window.setTimeout(function () {
            native.setAttribute('style', previous);
        }, 600);
    }

    function syncNativeButtons(view, holder) {
        holder.innerHTML = '';
        MIRRORED_BUTTONS.forEach(function (cls) {
            var native = view.querySelector('.mainDetailButtons .' + cls);
            if (!native || native.classList.contains('hide')) {
                return;
            }
            var iconNode = native.querySelector('.material-icons');
            var iconName = iconNode ? Array.prototype.filter.call(iconNode.classList, function (c) {
                return c !== 'material-icons' && c !== 'detailButton-icon';
            })[0] || iconNode.textContent.trim() : 'more_vert';
            var label = native.getAttribute('title') || (native.querySelector('.detailButton-text') || {}).textContent || '';
            var mirror = button('lr-round lr-native-btn', iconName, null, function () {
                clickNative(native, mirror);
            });
            mirror.title = label;
            mirror.setAttribute('aria-label', label);
            if (cls === 'btnPlaystate' && native.classList.contains('playstatebutton-played')) {
                mirror.classList.add('lr-on');
            }
            holder.appendChild(mirror);
        });
    }

    function attachNativeButtons(view, page) {
        var holder = page.querySelector('.lr-native-actions');
        var buttons = view.querySelector('.mainDetailButtons');
        if (!holder || !buttons) {
            return;
        }
        syncNativeButtons(view, holder);
        if (window.MutationObserver) {
            var timer = null;
            var observer = new MutationObserver(function () {
                window.clearTimeout(timer);
                timer = window.setTimeout(function () {
                    if (!document.body.contains(page)) {
                        observer.disconnect();
                        return;
                    }
                    syncNativeButtons(view, holder);
                }, 100);
            });
            observer.observe(buttons, { attributes: true, attributeFilter: ['class', 'title'], subtree: true, childList: true });
        }
    }

    function onDetailShow(view) {
        var id = detailIdFromHash();
        var wanted = id && tidyWanted();
        var nativeOnce = null;
        try {
            nativeOnce = window.sessionStorage.getItem('lrWeb.nativeDetail');
            if (nativeOnce === id) {
                window.sessionStorage.removeItem('lrWeb.nativeDetail');
            }
        } catch (e) {
            nativeOnce = null;
        }
        if (!wanted || nativeOnce === id) {
            showNativeDetail(view);
            return;
        }
        var existing = view.querySelector(':scope > .lr-detail-page');
        if (existing && existing.dataset.id === id) {
            view.classList.add('lr-detail-active');
            return;
        }
        showNativeDetail(view);
        getJson(itemsQuery('Items/' + id).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines'))
            .then(function (item) {
                if (DETAIL_TYPES.indexOf(item.Type) < 0 || detailIdFromHash() !== id) {
                    return null;
                }
                // Staffel: Serie zeigen, mit dieser Staffel ausgewählt
                if (item.Type === 'Season' && item.SeriesId) {
                    return getJson(itemsQuery('Items/' + item.SeriesId).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines'))
                        .then(function (series) {
                            return { item: series, seasonId: item.Id };
                        });
                }
                return { item: item };
            })
            .then(function (result) {
                if (!result || detailIdFromHash() !== id) {
                    return;
                }
                var item = result.item;
                var page = el('div', 'lr-detail-page lr-dialog');
                page.dataset.id = id;
                renderInfo(page, item, function () {
                    showNativeDetail(view);
                }, { seasonId: result.seasonId });
                view.insertBefore(page, view.firstChild);
                attachNativeButtons(view, page);
                view.classList.add('lr-detail-active');
                window.scrollTo(0, 0);
            })
            .catch(function () {
                showNativeDetail(view);
            });
    }

    // ---------------------------------------------------------------- Welten-Seite

    function openWorld(world) {
        var dialog = overlayShell('lr-world-page');
        var heroItem = { Id: world.ImageItemId || '', Name: world.Name, Type: 'World', TrailerId: world.TrailerId };
        var hero = heroElement(heroItem, {
            image: world.ImageItemId ? imageUrl(world.ImageItemId, 'Backdrop', null, 1920) : null,
            logo: world.LogoUrl ? url(world.LogoUrl) : null,
            title: world.Name,
            meta: world.ItemCount + ' Titel',
            overview: null,
            actions: false,
            trailerId: world.TrailerId,
            trailer: false
        });
        dialog.appendChild(hero);
        var controls = el('div', 'lr-world-controls');
        var filter = 'all';
        var sort = 'year';
        var chips = el('div', 'lr-chips');
        [['all', 'Alle'], ['movies', 'Filme'], ['series', 'Serien']].forEach(function (pair) {
            var chip = button('lr-chip' + (pair[0] === filter ? ' lr-chip-on' : ''), null, pair[1], function () {
                filter = pair[0];
                chips.querySelectorAll('.lr-chip').forEach(function (c) {
                    c.classList.toggle('lr-chip-on', c === chip);
                });
                load();
            });
            chips.appendChild(chip);
        });
        controls.appendChild(chips);
        var select = el('select', 'lr-select');
        [['year', 'Neueste zuerst'], ['year-asc', 'Älteste zuerst'], ['added', 'Neu hinzugefügt'], ['rating', 'Bewertung'], ['name', 'Name']].forEach(function (pair) {
            var option = el('option', null, pair[1]);
            option.value = pair[0];
            select.appendChild(option);
        });
        select.addEventListener('change', function () {
            sort = select.value;
            load();
        });
        controls.appendChild(select);
        dialog.appendChild(controls);
        var grid = el('div', 'lr-grid lr-world-grid');
        dialog.appendChild(grid);

        function load() {
            grid.innerHTML = '';
            grid.appendChild(el('div', 'lr-spinner'));
            var query = 'LocalRecs/Worlds/' + world.Id + '/Items?limit=500&sort=' + sort + (filter !== 'all' ? '&type=' + filter : '');
            getJson(query).then(function (result) {
                var ids = itemsOf(result).map(function (i) {
                    return i.Id;
                });
                if (!ids.length) {
                    return [];
                }
                // Titel über Jellyfin laden (Bilder, Benutzerdaten) und Reihenfolge der Welt beibehalten
                return getJson(itemsQuery('Items?Ids=' + ids.join(','))).then(function (items) {
                    var byId = {};
                    itemsOf(items).forEach(function (i) {
                        byId[i.Id] = i;
                    });
                    return ids.map(function (id) {
                        return byId[id];
                    }).filter(Boolean);
                });
            }).then(function (items) {
                grid.innerHTML = '';
                if (!items.length) {
                    grid.appendChild(el('div', 'lr-empty', 'Keine Titel gefunden.'));
                }
                items.forEach(function (item) {
                    grid.appendChild(posterCard(item));
                });
            }).catch(function () {
                grid.innerHTML = '';
                grid.appendChild(el('div', 'lr-empty', 'Die Welt konnte nicht geladen werden.'));
            });
        }

        load();
    }

    // ---------------------------------------------------------------- Daten der Startseite

    function shuffled(list) {
        var copy = list.slice();
        for (var i = copy.length - 1; i > 0; i--) {
            var j = Math.floor(Math.random() * (i + 1));
            var t = copy[i];
            copy[i] = copy[j];
            copy[j] = t;
        }
        return copy;
    }

    function sessionChoice(key, create) {
        try {
            var stored = window.sessionStorage.getItem(key);
            if (stored) {
                return JSON.parse(stored);
            }
            var value = create();
            window.sessionStorage.setItem(key, JSON.stringify(value));
            return value;
        } catch (e) {
            return create();
        }
    }

    function safe(promise) {
        return promise.catch(function () {
            return null;
        });
    }

    // Lädt alle Reihen gleichzeitig. Ergebnis: Liste von { key, title, kind, items }
    function loadRows(user) {
        var uid = user.Id;
        var liveTv = user.Policy && user.Policy.EnableLiveTvAccess;
        var rows = [];
        var add = function (key, title, kind, promise) {
            var entry = { key: key, title: title, kind: kind, items: [] };
            rows.push(entry);
            return safe(promise).then(function (result) {
                entry.items = itemsOf(result).map(slim);
            });
        };
        var tasks = [];
        tasks.push(safe(getJson('LocalRecs/Recommendations/Reasons')).then(function (reasons) {
            state.reasons = reasons || {};
        }));
        tasks.push(add('resume', 'Weiterschauen für ' + user.Name, 'landscape', getJson(itemsQuery('UserItems/Resume?MediaTypes=Video&Limit=' + ROW_LIMIT))));
        tasks.push(add('nextup', 'Als Nächstes', 'landscape', getJson(itemsQuery('Shows/NextUp?Limit=' + ROW_LIMIT + '&EnableResumable=false'))));
        tasks.push(add('movies', 'Empfohlene Filme für dich', 'landscape', getJson('LocalRecs/Recommendations?type=movies&limit=' + ROW_LIMIT)));
        tasks.push(add('series', 'Empfohlene Serien für dich', 'landscape', getJson('LocalRecs/Recommendations?type=series&limit=' + ROW_LIMIT)));
        if (liveTv) {
            tasks.push(add('livetv', 'Heute im TV für dich', 'landscape', getJson('LocalRecs/Recommendations?type=livetv&limit=' + ROW_LIMIT)));
        }
        tasks.push(add('top10', 'Top 10 bei euch', 'top10', getJson('LocalRecs/Top10?limit=10')));
        var worldsEntry = { key: 'worlds', title: 'Welten', kind: 'worlds', items: [] };
        rows.push(worldsEntry);
        tasks.push(safe(getJson('LocalRecs/Worlds')).then(function (worlds) {
            worldsEntry.items = worlds || [];
        }));
        if (liveTv) {
            tasks.push(add('onnow', 'Jetzt im TV', 'landscape', getJson('LiveTv/Programs/Recommended?userId=' + uid
                + '&IsAiring=true&limit=' + ROW_LIMIT + '&EnableImageTypes=Primary,Thumb,Backdrop&Fields=ChannelInfo,PrimaryImageAspectRatio')));
        }
        var latestPlaceholder = { key: 'latest', title: '', kind: 'group', items: [] };
        rows.push(latestPlaceholder);
        var genresPlaceholder = { key: 'genres', title: '', kind: 'group', items: [] };
        rows.push(genresPlaceholder);
        var librariesEntry = { key: 'libraries', title: 'Bibliotheken', kind: 'libraries', items: [] };
        rows.push(librariesEntry);

        tasks.push(safe(getJson('UserViews?userId=' + uid)).then(function (views) {
            var all = itemsOf(views);
            librariesEntry.items = all.map(function (v) {
                return { Id: v.Id, Name: v.Name, CollectionType: v.CollectionType, ImageTags: v.ImageTags };
            });
            var media = all.filter(function (v) {
                return v.CollectionType === 'movies' || v.CollectionType === 'tvshows';
            });
            return Promise.all(media.map(function (view) {
                var entry = { key: 'latest-' + view.Id, title: 'Neu in ' + view.Name, kind: 'landscape', items: [] };
                latestPlaceholder.items.push(entry);
                return safe(getJson(itemsQuery('Items/Latest?ParentId=' + view.Id + '&Limit=' + ROW_LIMIT + '&GroupItems=true'))).then(function (result) {
                    entry.items = itemsOf(result).map(slim);
                });
            }));
        }));

        tasks.push(safe(getJson('Genres?userId=' + uid + '&IncludeItemTypes=Movie,Series&Recursive=true&EnableTotalRecordCount=false')).then(function (result) {
            var all = itemsOf(result).map(function (g) {
                return g.Name;
            }).filter(Boolean);
            var german = all.filter(function (n) {
                return GERMAN_GENRES.some(function (g) {
                    return g.toLowerCase() === n.toLowerCase();
                });
            });
            var names = german.length >= 3 ? german : all;
            // Mehr Kandidaten laden als nötig – Genres mit zu wenigen Titeln fallen weg
            var picked = sessionChoice('lrWeb.genres2.' + uid, function () {
                return shuffled(names).slice(0, GENRE_ROWS * 2);
            }).filter(function (n) {
                return names.indexOf(n) >= 0;
            });
            var entries = picked.map(function (genre) {
                return { key: 'genre-' + genre, title: genre, kind: 'landscape', items: [] };
            });
            return Promise.all(entries.map(function (entry) {
                return safe(getJson(itemsQuery('Items?Genres=' + encodeURIComponent(entry.title)
                    + '&IncludeItemTypes=Movie,Series&Recursive=true&SortBy=Random&Limit=' + ROW_LIMIT))).then(function (r) {
                    entry.items = itemsOf(r).map(slim);
                });
            })).then(function () {
                entries.filter(function (entry) {
                    return entry.items.length >= GENRE_MIN_ITEMS;
                }).slice(0, GENRE_ROWS).forEach(function (entry) {
                    genresPlaceholder.items.push(entry);
                });
            });
        }));

        return Promise.all(tasks).then(function () {
            var flat = [];
            rows.forEach(function (row) {
                if (row.kind === 'group') {
                    row.items.forEach(function (sub) {
                        flat.push(sub);
                    });
                } else {
                    flat.push(row);
                }
            });
            return flat;
        });
    }

    function pickHero(rows, uid) {
        var candidates = [];
        rows.forEach(function (row) {
            if (row.key === 'movies' || row.key === 'series') {
                row.items.slice(0, 6).forEach(function (item) {
                    if (item.BackdropImageTags && item.BackdropImageTags.length) {
                        candidates.push(item);
                    }
                });
            }
        });
        if (!candidates.length) {
            rows.forEach(function (row) {
                if (row.kind === 'landscape' || row.kind === 'top10') {
                    row.items.forEach(function (item) {
                        if ((item.Type === 'Movie' || item.Type === 'Series') && item.BackdropImageTags && item.BackdropImageTags.length) {
                            candidates.push(item);
                        }
                    });
                }
            });
        }
        if (!candidates.length) {
            return null;
        }
        var heroId = sessionChoice('lrWeb.hero.' + uid, function () {
            return candidates[Math.floor(Math.random() * Math.min(candidates.length, 8))].Id;
        });
        return candidates.filter(function (c) {
            return c.Id === heroId;
        })[0] || candidates[0];
    }

    // ---------------------------------------------------------------- Aufbau der Startseite

    function buildRow(row) {
        if (!row.items || !row.items.length) {
            return null;
        }
        var cards;
        if (row.kind === 'top10') {
            cards = row.items.slice(0, 10).map(function (item, i) {
                return top10Card(item, i + 1);
            });
            return rowElement(row.title, cards, 'lr-row-top10');
        }
        if (row.kind === 'worlds') {
            return rowElement(row.title, row.items.map(worldCard), 'lr-row-worlds');
        }
        if (row.kind === 'libraries') {
            return rowElement(row.title, row.items.map(libraryCard), 'lr-row-libraries');
        }
        if (phoneLayout() && row.key !== 'livetv' && row.key !== 'onnow') {
            return rowElement(row.title, row.items.map(portraitCard), 'lr-row-portrait');
        }
        var withReason = row.key === 'movies' || row.key === 'series';
        return rowElement(row.title, row.items.map(function (item) {
            return landscapeCard(item, withReason);
        }));
    }

    function render(root, data, uid) {
        var signature = function (row) {
            return (phoneLayout() ? 'p' : 'w') + JSON.stringify(row.items.map(function (i) {
                return [i.Id, i.UserData && i.UserData.PlayedPercentage, i.ItemCount, i.TrailerId];
            }));
        };
        var hero = pickHero(data.rows, uid);
        var heroHolder = root.querySelector('.lr-hero-holder');
        if (!heroHolder) {
            heroHolder = el('div', 'lr-hero-holder');
            root.appendChild(heroHolder);
        }
        if (hero && (heroHolder.dataset.id !== hero.Id || heroHolder.dataset.layout !== (phoneLayout() ? 'phone' : 'wide'))) {
            stopHeroVideo();
            heroHolder.innerHTML = '';
            heroHolder.dataset.id = hero.Id;
            heroHolder.dataset.layout = phoneLayout() ? 'phone' : 'wide';
            heroHolder.appendChild(heroElement(hero, { badge: state.reasons[hero.Id], card: true, overview: phoneLayout() ? null : undefined }));
        }
        root.classList.toggle('lr-no-hero', !hero);
        if (!root.querySelector('.lr-topchips')) {
            root.insertBefore(topChips(data.rows), root.firstChild);
        }
        var rowsHolder = root.querySelector('.lr-rows');
        if (!rowsHolder) {
            rowsHolder = el('div', 'lr-rows');
            root.appendChild(rowsHolder);
        }
        // Reihe für Reihe ersetzen – unveränderte Reihen bleiben stehen (kein Springen der Scrollposition)
        var existing = {};
        rowsHolder.querySelectorAll(':scope > .lr-row').forEach(function (node) {
            existing[node.dataset.key] = node;
        });
        var previous = null;
        data.rows.forEach(function (row) {
            var sig = signature(row);
            var node = existing[row.key];
            delete existing[row.key];
            if (node && node.dataset.sig === sig) {
                previous = node;
                return;
            }
            var fresh = buildRow(row);
            if (node) {
                if (fresh) {
                    var oldTrack = node.querySelector('.lr-track');
                    var scroll = oldTrack ? oldTrack.scrollLeft : 0;
                    node.replaceWith(fresh);
                    var newTrack = fresh.querySelector('.lr-track');
                    if (newTrack) {
                        newTrack.scrollLeft = scroll;
                    }
                } else {
                    node.remove();
                }
            } else if (fresh) {
                if (previous) {
                    previous.after(fresh);
                } else {
                    rowsHolder.prepend(fresh);
                }
            }
            if (fresh) {
                fresh.dataset.key = row.key;
                fresh.dataset.sig = sig;
                previous = fresh;
            } else if (node) {
                previous = previous || null;
            }
        });
        Object.keys(existing).forEach(function (key) {
            existing[key].remove();
        });
        if (!root.querySelector('.lr-footer')) {
            var footer = el('div', 'lr-footer');
            footer.appendChild(button('lr-btn-link', 'dashboard', 'Klassische Startseite anzeigen', function () {
                storageSet('lrWeb.classic.' + uid, '1');
                deactivate();
                addClassicSwitch();
                tidyHeader();
                updateTabBar();
            }));
            root.appendChild(footer);
        }
    }

    function topChips(rows) {
        var bar = el('div', 'lr-topchips');
        var libraries = (rows.filter(function (r) {
            return r.key === 'libraries';
        })[0] || { items: [] }).items;
        var firstOf = function (type) {
            return libraries.filter(function (v) {
                return v.CollectionType === type;
            })[0];
        };
        var series = firstOf('tvshows');
        var movies = firstOf('movies');
        if (series) {
            bar.appendChild(button('lr-chip', null, 'Serien', function () {
                openLibrary(series);
            }));
        }
        if (movies) {
            bar.appendChild(button('lr-chip', null, 'Filme', function () {
                openLibrary(movies);
            }));
        }
        bar.appendChild(button('lr-chip', null, 'Meine Liste', function () {
            navigate('/home?tab=1');
        }));
        return bar;
    }

    function skeleton(root) {
        if (root.querySelector('.lr-rows')) {
            return;
        }
        var holder = el('div', 'lr-hero-holder');
        holder.appendChild(el('div', 'lr-hero lr-hero-skeleton'));
        root.appendChild(holder);
        var rows = el('div', 'lr-rows');
        for (var r = 0; r < 3; r++) {
            var row = el('section', 'lr-row lr-row-skeleton');
            row.appendChild(el('div', 'lr-row-title lr-skel-title'));
            var track = el('div', 'lr-track');
            for (var c = 0; c < 7; c++) {
                track.appendChild(el('div', 'lr-card lr-card-landscape lr-skel-card'));
            }
            row.appendChild(track);
            rows.appendChild(row);
        }
        root.appendChild(rows);
    }

    function cacheKey(uid) {
        return 'lrWeb.home.' + uid;
    }

    function readCache(uid) {
        try {
            var data = JSON.parse(storageGet(cacheKey(uid)) || 'null');
            return data && data.v === CACHE_VERSION ? data : null;
        } catch (e) {
            return null;
        }
    }

    function refresh(force) {
        var root = state.root;
        var uid = userId();
        if (!root || !uid || state.loading) {
            return;
        }
        if (!force && Date.now() - state.lastLoad < 30000) {
            return;
        }
        state.loading = true;
        api().getCurrentUser().then(function (user) {
            return loadRows(user);
        }).then(function (rows) {
            state.lastLoad = Date.now();
            var data = { v: CACHE_VERSION, rows: rows, reasons: state.reasons };
            if (state.root === root) {
                if (root.querySelector('.lr-skel-card')) {
                    root.innerHTML = '';
                }
                render(root, data, uid);
            }
            storageSet(cacheKey(uid), JSON.stringify(data));
        }).catch(function (err) {
            console.warn('LocalRecs Web: Startseite konnte nicht geladen werden', err);
        }).then(function () {
            state.loading = false;
        });
    }

    // ---------------------------------------------------------------- Ein- und Ausblenden

    function homeTab() {
        return document.querySelector('#indexPage:not(.hide) #homeTab');
    }

    function headerElement() {
        return document.querySelector('header.MuiAppBar-root') || document.querySelector('.skinHeader');
    }

    function updateHeader() {
        var header = headerElement();
        if (header && header.offsetHeight) {
            document.documentElement.style.setProperty('--lr-header-h', header.offsetHeight + 'px');
        }
        var active = !!(state.root && document.body.contains(state.root) && state.root.offsetParent !== null);
        document.body.classList.toggle('lr-home', active);
        document.body.classList.toggle('lr-scrolled', !active || window.scrollY > 40);
    }

    function deactivate() {
        var tab = document.querySelector('#homeTab');
        if (tab) {
            tab.classList.remove('lr-active');
        }
        var page = document.querySelector('#indexPage');
        if (page) {
            page.classList.remove('lr-page');
        }
        if (state.root) {
            state.root.remove();
            state.root = null;
        }
        stopHeroVideo();
        document.body.classList.remove('lr-home', 'lr-scrolled');
    }

    function addClassicSwitch() {
        var tab = homeTab();
        if (!tab || tab.querySelector('.lr-classic-switch')) {
            return;
        }
        var bar = el('div', 'lr-classic-switch');
        bar.appendChild(button('lr-btn-link', 'movie_filter', 'Startseite im Netflix-Stil anzeigen', function () {
            storageSet('lrWeb.classic.' + userId(), null);
            bar.remove();
            activate();
            tidyHeader();
            updateTabBar();
        }));
        tab.appendChild(bar);
    }

    function activate() {
        var tab = homeTab();
        var uid = userId();
        if (!tab || !uid || !state.config || !state.config.HomeEnabled) {
            return;
        }
        if (storageGet('lrWeb.classic.' + uid) === '1') {
            addClassicSwitch();
            return;
        }
        tab.classList.add('lr-active');
        document.querySelector('#indexPage').classList.add('lr-page');
        if (!state.root || !tab.contains(state.root) || state.root.dataset.user !== uid) {
            if (state.root) {
                state.root.remove();
            }
            state.root = el('div', 'lr-home-root');
            state.root.dataset.user = uid;
            tab.insertBefore(state.root, tab.firstChild);
            state.lastLoad = 0;
            var cached = readCache(uid);
            if (cached) {
                state.reasons = cached.reasons || {};
                render(state.root, cached, uid);
            } else {
                skeleton(state.root);
            }
        } else {
            // Zurück auf der Startseite: Trailer wieder starten
            var hero = state.root.querySelector('.lr-hero-holder > .lr-hero');
            var heroId = state.root.querySelector('.lr-hero-holder');
            if (hero && heroId && !hero.querySelector('.lr-hero-video')) {
                var cachedData = readCache(uid);
                var heroItem = cachedData ? pickHero(cachedData.rows, uid) : null;
                if (heroItem) {
                    loadTrailer(hero, heroItem);
                }
            }
        }
        updateHeader();
        refresh(false);
    }

    // ---------------------------------------------------------------- Kopfleiste aufräumen (Netflix-Stil)
    // Links: Logo + Startseite, Serien, Filme, Live-TV, Meine Liste, Bibliotheken ▾. Rechts: Suche, Cast (nur PC), Profil.
    // Alle anderen Symbole (SyncPlay, Symbole anderer Plugins) wandern ins Profilmenü.

    var navState = { views: [], observer: null };

    function tidyWanted() {
        var hash = window.location.hash || '';
        return !!(state.config && state.config.HomeEnabled && userId()
            && storageGet('lrWeb.classic.' + userId()) !== '1'
            && !/^#\/(dashboard|video|configurationpage)/.test(hash));
    }

    function headerToolbar() {
        return document.querySelector('header.MuiAppBar-root .MuiToolbar-root');
    }

    function isKeptButton(node) {
        if (node.querySelector('.MuiAvatar-root') || node.closest('.lr-topnav')) {
            return true;
        }
        if (node.querySelector('svg[data-testid="SearchIcon"]')) {
            return true;
        }
        if (node.querySelector('svg[data-testid="MenuIcon"], svg[data-testid="ArrowBackIcon"]')) {
            return true;
        }
        return !phoneLayout() && !!node.querySelector('svg[data-testid^="Cast"]');
    }

    function tuckedButtons() {
        var toolbar = headerToolbar();
        if (!toolbar) {
            return [];
        }
        var nav = toolbar.querySelector(':scope > .MuiStack-root');
        // Logo und Bibliotheks-Links (MUI-Buttons im linken Bereich) bleiben; Plugin-Knöpfe dort (z. B. der Würfel
        // von Jellyfin Enhanced auf dem Handy) werden ebenfalls weggeräumt
        return Array.prototype.filter.call(toolbar.querySelectorAll('button, a'), function (node) {
            var navLink = nav && nav.contains(node) && node.classList.contains('MuiButton-root');
            return !navLink && !isKeptButton(node) && !node.parentElement.closest('button, a');
        });
    }

    function tidyHeader() {
        var wanted = tidyWanted() && !!headerToolbar();
        document.body.classList.toggle('lr-tidy-header', wanted);
        document.querySelectorAll('.lr-tucked').forEach(function (node) {
            if (!wanted || isKeptButton(node)) {
                node.classList.remove('lr-tucked');
            }
        });
        var topnav = document.querySelector('.lr-topnav');
        if (!wanted) {
            if (topnav) {
                topnav.remove();
            }
            return;
        }
        tuckedButtons().forEach(function (node) {
            node.classList.add('lr-tucked');
        });
        // Kopfleiste beobachten: Plugins (z. B. Jellyfin Enhanced) fügen ihre Knöpfe später ein
        var header = document.querySelector('header.MuiAppBar-root');
        if (window.MutationObserver && navState.header !== header) {
            if (navState.observer) {
                navState.observer.disconnect();
            }
            navState.header = header;
            navState.observer = new MutationObserver(function () {
                window.clearTimeout(navState.timer);
                navState.timer = window.setTimeout(tidyHeader, 50);
            });
            navState.observer.observe(header, { childList: true, subtree: true });
        }
        var toolbar = headerToolbar();
        var logo = toolbar.querySelector(':scope > .MuiStack-root > a:first-child');
        if (phoneLayout() || !logo) {
            if (topnav) {
                topnav.remove();
            }
            return;
        }
        if (!topnav) {
            topnav = buildTopNav();
            document.body.appendChild(topnav);
        }
        var rect = logo.getBoundingClientRect();
        var bar = toolbar.getBoundingClientRect();
        topnav.style.left = Math.round(rect.right + 12) + 'px';
        topnav.style.top = Math.round(bar.top) + 'px';
        topnav.style.height = Math.round(bar.height) + 'px';
        markTopNav(topnav);
    }

    function firstView(type) {
        return navState.views.filter(function (v) {
            return v.CollectionType === type;
        })[0];
    }

    function buildTopNav() {
        var nav = el('nav', 'lr-topnav');
        var add = function (id, label, onClick) {
            var b = button('lr-topnav-link', null, label, onClick);
            b.dataset.nav = id;
            nav.appendChild(b);
        };
        add('home', 'Startseite', function () {
            navigate('/home');
        });
        if (firstView('tvshows')) {
            add('series', 'Serien', function () {
                openLibrary(firstView('tvshows'));
            });
        }
        if (firstView('movies')) {
            add('movies', 'Filme', function () {
                openLibrary(firstView('movies'));
            });
        }
        if (state.liveTv) {
            add('livetv', 'Live-TV', function () {
                navigate('/livetv');
            });
        }
        add('list', 'Meine Liste', function () {
            navigate('/home?tab=1');
        });
        var moreWrap = el('div', 'lr-topnav-more');
        var more = button('lr-topnav-link', 'arrow_drop_down', 'Bibliotheken', function () {
            moreWrap.classList.toggle('lr-open');
        });
        more.classList.add('lr-topnav-dropdown');
        moreWrap.appendChild(more);
        var menu = el('div', 'lr-topnav-menu');
        navState.views.forEach(function (view) {
            menu.appendChild(button('lr-topnav-item', null, view.Name, function () {
                moreWrap.classList.remove('lr-open');
                openLibrary(view);
            }));
        });
        moreWrap.appendChild(menu);
        nav.appendChild(moreWrap);
        document.addEventListener('click', function (e) {
            if (!moreWrap.contains(e.target)) {
                moreWrap.classList.remove('lr-open');
            }
        });
        return nav;
    }

    function markTopNav(nav) {
        var hash = window.location.hash || '';
        var active = /tab=1/.test(hash) ? 'list'
            : /^#\/home/.test(hash) ? 'home'
                : /^#\/livetv/.test(hash) ? 'livetv'
                    : /collectionType=tvshows|^#\/tv\b/.test(hash) ? 'series'
                        : /collectionType=movies|^#\/movies/.test(hash) ? 'movies' : '';
        nav.querySelectorAll('.lr-topnav-link').forEach(function (b) {
            b.classList.toggle('lr-on', b.dataset.nav === active);
        });
    }

    // Weggeräumte Symbole oben im Profilmenü anbieten
    // Alle weggeräumten Knöpfe – auch solche, die ein anderes Plugin gerade selbst versteckt hat
    // (Jellyfin Enhanced blendet z. B. Knöpfe in seine eigene Leiste aus). Ohne doppelte Einträge.
    function menuSources() {
        var seen = {};
        var result = [];
        var candidates = tuckedButtons();
        var dice = document.getElementById('randomItemButton');
        if (dice && candidates.indexOf(dice) < 0) {
            candidates.unshift(dice);
        }
        candidates.forEach(function (node) {
            if (node.classList.contains('je-header-launcher') || node.closest('.je-launcher-panel')) {
                return;
            }
            var label = (node.getAttribute('aria-label') || node.getAttribute('title') || node.dataset.headerLabel || node.textContent || '').trim() || 'Funktion';
            if (seen[label]) {
                return;
            }
            seen[label] = true;
            result.push({ node: node, label: label });
        });
        return result;
    }

    function extendUserMenu(list) {
        if (!list) {
            return;
        }
        // Das Menü bleibt im DOM – bei jedem Öffnen neu aufbauen, damit später hinzugekommene Knöpfe dabei sind
        list.querySelectorAll('.lr-menu-item, .lr-menu-divider').forEach(function (node) {
            node.remove();
        });
        var sources = menuSources();
        var tucked = sources.map(function (entry) {
            return entry.node;
        });
        sources.slice().reverse().forEach(function (entry) {
            var node = entry.node;
            var label = entry.label;
            var li = el('li', 'lr-menu-item MuiButtonBase-root MuiMenuItem-root');
            li.setAttribute('role', 'menuitem');
            li.tabIndex = 0;
            var iconBox = el('span', 'lr-menu-icon');
            var svg = node.querySelector('svg, img, .material-icons, i');
            if (svg) {
                iconBox.appendChild(svg.cloneNode(true));
            }
            li.appendChild(iconBox);
            li.appendChild(el('span', null, label));
            onActivate(li, function () {
                var backdrop = document.querySelector('#app-user-menu .MuiBackdrop-root');
                if (backdrop) {
                    backdrop.click();
                }
                window.setTimeout(function () {
                    node.click();
                }, 150);
            });
            list.insertBefore(li, list.firstChild);
        });
        if (tucked.length) {
            var divider = el('li', 'lr-menu-divider');
            divider.setAttribute('role', 'separator');
            list.insertBefore(divider, list.children[tucked.length]);
        }
    }

    // Beim Öffnen des Profilmenüs (Klick auf das Profilbild) die Einträge ergänzen
    document.addEventListener('click', function (e) {
        var target = e.target && e.target.closest ? e.target.closest('header.MuiAppBar-root button') : null;
        if (!target || !target.querySelector('.MuiAvatar-root') || !document.body.classList.contains('lr-tidy-header')) {
            return;
        }
        var tries = 0;
        var timer = window.setInterval(function () {
            var list = document.querySelector('#app-user-menu ul');
            tries++;
            if (list || tries > 20) {
                window.clearInterval(timer);
                extendUserMenu(list);
            }
        }, 25);
    }, true);

    // ---------------------------------------------------------------- Überspringen (Intro, Abspann, Rückblick) mit Countdown

    var SKIP_COUNTDOWN_MS = 5000;
    var SKIP_LABELS = {
        Intro: 'Intro überspringen',
        Outro: 'Abspann überspringen',
        Recap: 'Rückblick überspringen',
        Preview: 'Vorschau überspringen',
        Commercial: 'Werbung überspringen'
    };
    var skip = { timer: null, itemId: null, segments: [], dismissed: {}, active: null, offset: 0, lastSession: 0 };

    function skipWanted() {
        return !!(state.config && (state.config.HomeEnabled || state.config.ThemeEnabled));
    }

    function parseClock(text) {
        var parts = (text || '').trim().split(':').map(function (p) {
            return parseInt(p, 10);
        });
        if (!parts.length || parts.some(isNaN)) {
            return null;
        }
        return parts.reduce(function (total, p) {
            return total * 60 + p;
        }, 0);
    }

    function currentVideo() {
        return document.querySelector('video.htmlvideoplayer') || document.querySelector('.videoPlayerContainer video');
    }

    // Absolute Position in Sekunden. Beim Transkodieren kann video.currentTime versetzt sein – die
    // Zeitanzeige des Players (.osdPositionText) ist absolut und dient zum Abgleich.
    function playbackPosition(video) {
        var shown = parseClock((document.querySelector('#videoOsdPage .osdPositionText') || {}).textContent);
        if (shown !== null && Math.abs(shown - (video.currentTime + skip.offset)) > 3) {
            skip.offset = shown - video.currentTime;
        }
        return video.currentTime + skip.offset;
    }

    function refreshSkipItem() {
        if (Date.now() - skip.lastSession < 4000) {
            return;
        }
        skip.lastSession = Date.now();
        ownSession().then(function (session) {
            var item = session && session.NowPlayingItem;
            var id = item ? item.Id : null;
            if (!id || id === skip.itemId) {
                return;
            }
            skip.itemId = id;
            skip.segments = [];
            skip.dismissed = {};
            skip.offset = 0;
            hideSkip();
            getJson('MediaSegments/' + id + '?includeSegmentTypes=Intro&includeSegmentTypes=Outro&includeSegmentTypes=Recap&includeSegmentTypes=Preview')
                .then(function (result) {
                    if (skip.itemId === id) {
                        skip.segments = itemsOf(result).filter(function (seg) {
                            return SKIP_LABELS[seg.Type] && seg.EndTicks > seg.StartTicks;
                        });
                    }
                }).catch(function () {
                    // ohne Segmente kein Knopf
                });
        });
    }

    function hideSkip() {
        var box = document.querySelector('.lr-skip');
        if (box) {
            box.remove();
        }
        if (skip.active) {
            window.clearTimeout(skip.active.timer);
        }
        skip.active = null;
        document.body.classList.remove('lr-skip-active');
    }

    function doSkip(segment) {
        hideSkip();
        skip.dismissed[segment.Id || segment.StartTicks] = true;
        // Bevorzugt den Knopf von Jellyfin (springt über den Player, auch beim Transkodieren)
        var native = document.querySelector('.skip-button:not(.hide)');
        if (native) {
            native.click();
            return;
        }
        var video = currentVideo();
        if (video) {
            video.currentTime = Math.max(0, segment.EndTicks / 10000000 - skip.offset);
        }
    }

    function showSkip(segment) {
        var key = segment.Id || segment.StartTicks;
        if (skip.active && skip.active.key === key) {
            return;
        }
        hideSkip();
        var box = el('div', 'lr-skip');
        var btn = button('lr-skip-btn', 'skip_next', SKIP_LABELS[segment.Type], function () {
            doSkip(segment);
        });
        btn.insertBefore(el('span', 'lr-skip-fill'), btn.firstChild);
        btn.style.setProperty('--lr-skip-ms', SKIP_COUNTDOWN_MS + 'ms');
        var cancel = button('lr-skip-cancel', 'close', null, function () {
            skip.dismissed[key] = true;
            hideSkip();
        });
        cancel.title = 'Nicht überspringen';
        box.appendChild(btn);
        box.appendChild(cancel);
        document.body.appendChild(box);
        document.body.classList.add('lr-skip-active');
        window.requestAnimationFrame(function () {
            window.requestAnimationFrame(function () {
                btn.classList.add('lr-skip-counting');
            });
        });
        skip.active = {
            key: key,
            timer: window.setTimeout(function () {
                doSkip(segment);
            }, SKIP_COUNTDOWN_MS)
        };
    }

    function skipTick() {
        var video = currentVideo();
        if (!video || !document.querySelector('#videoOsdPage:not(.hide)')) {
            hideSkip();
            return;
        }
        refreshSkipItem();
        var pos = playbackPosition(video);
        var segment = skip.segments.filter(function (seg) {
            var start = seg.StartTicks / 10000000;
            var end = seg.EndTicks / 10000000;
            return pos >= start && pos < end - 1 && !skip.dismissed[seg.Id || seg.StartTicks];
        })[0];
        if (segment) {
            showSkip(segment);
        } else if (skip.active) {
            hideSkip();
        }
    }

    function startSkipWatcher() {
        if (!skipWanted() || skip.timer) {
            return;
        }
        skip.itemId = null;
        skip.lastSession = 0;
        skip.timer = window.setInterval(skipTick, 500);
    }

    function stopSkipWatcher() {
        window.clearInterval(skip.timer);
        skip.timer = null;
        skip.itemId = null;
        hideSkip();
    }

    document.addEventListener('keydown', function (e) {
        if (skip.active && (e.key === 'Escape' || e.key === 'Backspace')) {
            var box = document.querySelector('.lr-skip');
            if (box) {
                skip.dismissed[skip.active.key] = true;
                hideSkip();
                e.stopPropagation();
                e.preventDefault();
            }
        }
    }, true);

    var tabs = [
        { id: 'home', icon: 'home', label: 'Startseite', route: '/home', match: /^#\/home(\?(?!.*tab=1)|$)|^#?$/ },
        { id: 'search', icon: 'search', label: 'Suche', route: '/search', match: /^#\/search/ },
        { id: 'livetv', icon: 'live_tv', label: 'Live-TV', route: '/livetv', match: /^#\/livetv/, liveTv: true },
        { id: 'list', icon: 'favorite_border', label: 'Meine Liste', route: '/home?tab=1', match: /^#\/home\?.*tab=1/ },
        { id: 'more', icon: 'menu', label: 'Mehr', route: '/mypreferencesmenu', match: /^#\/(mypreferences|userprofile|dashboard)/ }
    ];

    function updateTabBar() {
        var bar = document.querySelector('.lr-tabbar');
        var wanted = !!(state.config && state.config.HomeEnabled && phoneLayout() && userId() && !isNativeIos
            && storageGet('lrWeb.classic.' + userId()) !== '1' && !document.querySelector('.videoPlayerContainer:not(.hide)'));
        document.body.classList.toggle('lr-with-tabbar', wanted);
        if (!wanted) {
            if (bar) {
                bar.remove();
            }
            return;
        }
        if (!bar) {
            bar = el('nav', 'lr-tabbar');
            tabs.forEach(function (tab) {
                if (tab.liveTv && !state.liveTv) {
                    return;
                }
                var b = button('lr-tab', tab.icon, tab.label, function () {
                    navigate(tab.route);
                });
                b.dataset.tab = tab.id;
                bar.appendChild(b);
            });
            document.body.appendChild(bar);
        }
        var hash = window.location.hash || '';
        bar.querySelectorAll('.lr-tab').forEach(function (b) {
            var tab = tabs.filter(function (t) {
                return t.id === b.dataset.tab;
            })[0];
            b.classList.toggle('lr-tab-on', tab.match.test(hash));
        });
    }

    function onViewShow(e) {
        window.setTimeout(updateTabBar, 0);
        window.setTimeout(tidyHeader, 0);
        var view = e && e.target;
        if (view && view.id === 'videoOsdPage') {
            startSkipWatcher();
        } else {
            stopSkipWatcher();
        }
        if (view && view.id === 'itemDetailPage') {
            onDetailShow(view);
        }
        if (view && view.id === 'indexPage') {
            window.setTimeout(activate, 0);
        } else {
            stopHeroVideo();
            closeOverlays();
            document.body.classList.remove('lr-home', 'lr-scrolled');
        }
    }

    // ---------------------------------------------------------------- Einstellungen und Start

    function applyTheme() {
        document.documentElement.classList.toggle('lr-theme', !!(state.config && state.config.ThemeEnabled));
    }

    function loadConfig() {
        var cached = storageGet('lrWeb.config');
        if (cached) {
            try {
                state.config = JSON.parse(cached);
                applyTheme();
            } catch (e) {
                state.config = null;
            }
        }
        return getJson('LocalRecs/Web/Config').then(function (config) {
            state.config = config || {};
            storageSet('lrWeb.config', JSON.stringify(state.config));
            applyTheme();
            if (!state.config.HomeEnabled) {
                deactivate();
            }
        });
    }

    function whenReady(callback) {
        var tries = 0;
        var timer = window.setInterval(function () {
            tries++;
            if (window.ApiClient && userId()) {
                window.clearInterval(timer);
                callback();
            } else if (tries > 600) {
                window.clearInterval(timer);
            }
        }, 200);
    }

    // Design (Farben) sofort aus dem Zwischenspeicher, damit nichts aufblitzt
    try {
        var early = JSON.parse(storageGet('lrWeb.config') || 'null');
        if (early && early.ThemeEnabled) {
            document.documentElement.classList.add('lr-theme');
        }
    } catch (e) {
        // ignorieren
    }

    document.addEventListener('viewshow', onViewShow);
    window.addEventListener('scroll', updateHeader, { passive: true });
    window.addEventListener('resize', hidePreview, { passive: true });
    window.addEventListener('hashchange', function () {
        window.setTimeout(updateTabBar, 0);
        window.setTimeout(tidyHeader, 0);
    });
    window.addEventListener('resize', function () {
        window.clearTimeout(navState.resizeTimer);
        navState.resizeTimer = window.setTimeout(tidyHeader, 100);
    }, { passive: true });
    var onLayoutChange = function () {
        updateTabBar();
        var cached = state.root ? readCache(userId()) : null;
        if (cached) {
            render(state.root, cached, userId());
        }
    };
    if (phoneQuery.addEventListener) {
        phoneQuery.addEventListener('change', onLayoutChange);
    } else if (phoneQuery.addListener) {
        phoneQuery.addListener(onLayoutChange);
    }
    document.addEventListener('scroll', hidePreview, { passive: true, capture: true });
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && overlayStack.length) {
            closeTopOverlay();
        }
    });

    var lastUser = null;
    whenReady(function () {
        loadConfig().catch(function () {
            // Plugin-Endpunkt nicht erreichbar – dann bleibt alles wie in Jellyfin
        }).then(function () {
            lastUser = userId();
            api().getCurrentUser().then(function (user) {
                state.liveTv = !!(user && user.Policy && user.Policy.EnableLiveTvAccess);
                updateTabBar();
                return getJson('UserViews?userId=' + userId());
            }).then(function (views) {
                navState.views = itemsOf(views).map(function (v) {
                    return { Id: v.Id, Name: v.Name, CollectionType: v.CollectionType };
                });
                var old = document.querySelector('.lr-topnav');
                if (old) {
                    old.remove();
                }
                tidyHeader();
            }).catch(function () {
                tidyHeader();
            });
            activate();
        });
        // Benutzerwechsel erkennen (Abmelden/Anmelden ohne Neuladen)
        window.setInterval(function () {
            var current = userId();
            if (current && current !== lastUser) {
                lastUser = current;
                deactivate();
                var oldNav = document.querySelector('.lr-topnav');
                if (oldNav) {
                    oldNav.remove();
                }
                loadConfig().catch(function () {
                    return null;
                }).then(activate);
            }
        }, 2000);
    });
})();
