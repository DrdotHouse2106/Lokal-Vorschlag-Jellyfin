/*
 * Quallenkinoplugin für Jellyfin – Startseite im Quallenkino-Stil für Jellyfin Web (Browser und Jellyfin-Apps für iOS/Android).
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

    var CACHE_VERSION = 3;
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
    // Jellyfin-iOS-App (WKWebView mit eigener NativeShell). Safari selbst hat weder NativeShell noch webkit.messageHandlers.
    var isAppleMobile = /iPhone|iPad|iPod/i.test(navigator.userAgent) || /Macintosh/.test(navigator.userAgent) && navigator.maxTouchPoints > 1;
    var isNativeIos = isAppleMobile && !!(window.NativeShell || window.ReactNativeWebView || window.webkit && window.webkit.messageHandlers);

    // Handy-Ansicht wie in Streaming-Apps: Hochformat-Poster, Hero als Karte, Tab-Leiste unten
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

    function postJson(path, body) {
        return api().ajax({ type: 'POST', url: url(path), data: JSON.stringify(body), contentType: 'application/json' });
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

    // ---------------------------------------------------------------- Diagnose (Einstellung „Diagnose“)
    // Schritte (Ansicht, Abspielen, Player, Video-Ereignisse) landen laufend im localStorage. Endet eine Seite ohne
    // „pagehide“ (WebView abgestürzt), schickt der nächste Start die letzten Schritte ins Serverlog.

    var diag = { queue: [], timer: null, page: Math.random().toString(36).slice(2, 8), steps: [], beat: -1, previous: null };
    try {
        // Vor dem ersten eigenen Schritt lesen – der überschreibt den Eintrag
        diag.previous = JSON.parse(storageGet('lrWeb.diag') || 'null');
    } catch (e) {
        diag.previous = null;
    }

    function diagOn() {
        if (state.config) {
            return !!state.config.Diagnostics;
        }
        try {
            return !!(JSON.parse(storageGet('lrWeb.config') || 'null') || {}).Diagnostics;
        } catch (e) {
            return false;
        }
    }

    function diagTime() {
        var d = new Date();
        return ('0' + d.getHours()).slice(-2) + ':' + ('0' + d.getMinutes()).slice(-2) + ':' + ('0' + d.getSeconds()).slice(-2)
            + '.' + ('00' + d.getMilliseconds()).slice(-3);
    }

    function diagSave() {
        storageSet('lrWeb.diag', JSON.stringify({ page: diag.page, clean: !!diag.clean, at: Date.now(), steps: diag.steps }));
    }

    // Lebenszeichen ersetzen das vorige, damit die eigentlichen Schritte nicht verdrängt werden
    function diagStep(text, heartbeat, send) {
        if (!diagOn()) {
            return;
        }
        var line = diagTime() + ' ' + text;
        if (send) {
            diagSend(line);
        }
        if (heartbeat && diag.beat >= 0 && diag.beat === diag.steps.length - 1) {
            diag.steps[diag.beat] = line;
        } else {
            diag.steps.push(line);
            if (diag.steps.length > 30) {
                diag.steps.shift();
            }
            diag.beat = heartbeat ? diag.steps.length - 1 : -1;
        }
        diagSave();
    }

    function diagSend(line) {
        if (!diagOn()) {
            return;
        }
        diag.queue.push(line);
        if (diag.queue.length > 60) {
            diag.queue.shift();
        }
        window.clearTimeout(diag.timer);
        diag.timer = window.setTimeout(diagFlush, 1500);
    }

    function diagFlush() {
        if (!diag.queue.length) {
            return;
        }
        if (!api() || !userId()) {
            diag.timer = window.setTimeout(diagFlush, 3000);
            return;
        }
        var lines = diag.queue.splice(0, diag.queue.length);
        postJson('LocalRecs/ClientLog', { Lines: lines }).catch(function () {
            // Diagnose darf nie stören
        });
    }

    function errorText(err) {
        if (!err) {
            return '?';
        }
        if (err.status) {
            return 'HTTP ' + err.status + (err.statusText ? ' ' + err.statusText : '');
        }
        return err.message || String(err);
    }

    function diagEnvironment() {
        var app = window.ExpoAppInfo ? window.ExpoAppInfo.appName + ' ' + window.ExpoAppInfo.appVersion : '-';
        var nativeVideo = window.ExpoAppSettings ? String(!!window.ExpoAppSettings.isNativeVideoPlayerEnabled) : '-';
        return 'App: ' + app + ', iOS-App erkannt: ' + isNativeIos + ', NativeShell: ' + !!window.NativeShell
            + ', nativer Videoplayer der App: ' + nativeVideo + ', Bildschirm ' + window.innerWidth + 'x' + window.innerHeight
            + ' @' + (window.devicePixelRatio || 1) + ', ' + navigator.userAgent;
    }

    function diagStart() {
        if (!diagOn() || diag.started) {
            return;
        }
        diag.started = true;
        var previous = diag.previous;
        diagSend('Start Quallenkino ' + ((state.config && state.config.Version) || '?') + ', Player: ' + playerWanted() + ' · ' + diagEnvironment());
        if (previous && previous.page !== diag.page && previous.steps && previous.steps.length && Date.now() - previous.at < 10 * 60000) {
            diagSend(previous.clean
                ? 'Vorherige Seite normal verlassen (Neuladen/Wechsel), letzte Schritte:'
                : 'Vorherige Seite OHNE pagehide beendet (Absturz bzw. WebView beendet?), letzte Schritte:');
            previous.steps.slice(previous.clean ? -6 : -30).forEach(function (step) {
                diagSend('  ' + step);
            });
        }
        diagStep('Diagnose aktiv, ' + window.location.hash);
        diagWatchEnhanced();
        diagWatchSeerrSection();
        // Vollbild-Umschaltung der iOS-App (verschiebt das Layout, sperrt bzw. erlaubt „Ziehen zum Neuladen“)
        ['enableFullscreen', 'disableFullscreen'].forEach(function (name) {
            var shell = window.NativeShell;
            if (shell && typeof shell[name] === 'function' && !shell[name].lrWrapped) {
                var original = shell[name];
                shell[name] = function () {
                    diagStep('NativeShell.' + name);
                    return original.apply(this, arguments);
                };
                shell[name].lrWrapped = true;
            }
        });
    }

    // Jellyseerr-Treffer von „Jellyfin Enhanced“ in der Suche: Anfragen (nur Pfad und Status, ohne Suchbegriff),
    // dessen Seerr-Meldungen und ob der Bereich auf der Suchseite ankommt – für Fehler, die nur in einer App auftreten
    function diagWatchEnhanced() {
        if (window.fetch && !window.fetch.lrWrapped) {
            var originalFetch = window.fetch;
            var wrapped = function (input) {
                var target = typeof input === 'string' ? input : (input && input.url) || '';
                var started = Date.now();
                var promise = originalFetch.apply(this, arguments);
                if (/\/JellyfinEnhanced\/jellyseerr\//i.test(target)) {
                    var path = target.split('?')[0].replace(/^.*\/JellyfinEnhanced\//i, '');
                    promise.then(function (response) {
                        diagSend('Enhanced ' + path + ' → HTTP ' + response.status + ' (' + (Date.now() - started) + ' ms)');
                    }, function (err) {
                        diagSend('Enhanced ' + path + ' → Fehler ' + errorText(err));
                    });
                }
                return promise;
            };
            wrapped.lrWrapped = true;
            window.fetch = wrapped;
        }
        ['log', 'warn', 'error'].forEach(function (level) {
            var original = console[level];
            if (typeof original !== 'function' || original.lrWrapped) {
                return;
            }
            var wrappedLog = function (first) {
                if (typeof first === 'string' && first.indexOf('Jellyfin Enhanced') >= 0 && /seerr/i.test(first)) {
                    // Zitierte Texte (z. B. der Suchbegriff) nicht ins Serverlog
                    diagSend('Enhanced ' + level + ': ' + first.replace(/^\W+/, '').replace(/"[^"]*"|„[^“]*“|'[^']*'/g, '"…"').slice(0, 200));
                }
                return original.apply(this, arguments);
            };
            wrappedLog.lrWrapped = true;
            console[level] = wrappedLog;
        });
    }

    // Wo der Jellyseerr-Bereich hinkommt und wer ihn wieder entfernt (Aufrufstelle), dazu Ansichtswechsel der Suchseite
    function diagWatchSeerrSection() {
        if (diag.seerrWatched || !window.MutationObserver || !document.body) {
            return;
        }
        diag.seerrWatched = true;
        var short = function (node) {
            if (!node || !node.tagName) {
                return '-';
            }
            return node.tagName.toLowerCase() + (node.id ? '#' + node.id : '') + (typeof node.className === 'string' && node.className
                ? '.' + node.className.trim().split(/\s+/).slice(0, 3).join('.') : '');
        };
        var caller = function () {
            var stack = (new Error().stack || '').split('\n').map(function (line) {
                return line.trim();
            }).filter(function (line) {
                return line && line.indexOf('localrecs-web') < 0 && line !== 'Error';
            });
            return stack.slice(0, 4).map(function (line) {
                return line.replace(/https?:\/\/[^\s)]*\/([^/?\s)]+)(\?[^:\s)]*)?/g, '$1');
            }).join(' < ') || '?';
        };
        var isSeerr = function (node) {
            return node && node.nodeType === 1 && (node.classList.contains('jellyseerr-section') || !!node.querySelector('.jellyseerr-section'));
        };
        new MutationObserver(function (records) {
            records.forEach(function (record) {
                [].forEach.call(record.addedNodes, function (node) {
                    if (node.nodeType === 1 && node.classList.contains('jellyseerr-section')) {
                        diagSend('Seerr-Bereich eingefügt in ' + short(record.target));
                    } else if (node.nodeType === 1 && (node.id === 'searchPage' || node.querySelector && node.querySelector('#searchPage'))) {
                        diagSend('Suchseite neu aufgebaut (' + short(record.target) + ')');
                    }
                });
                [].forEach.call(record.removedNodes, function (node) {
                    if (isSeerr(node)) {
                        diagSend('Seerr-Bereich entfernt: ' + short(node) + ' aus ' + short(record.target));
                    } else if (node.nodeType === 1 && (node.id === 'searchPage' || node.querySelector && node.querySelector('#searchPage'))) {
                        diagSend('Suchseite entfernt (' + short(record.target) + ')');
                    }
                });
            });
        }).observe(document.body, { childList: true, subtree: true });
        var originalRemove = Element.prototype.remove;
        Element.prototype.remove = function () {
            if (this.classList && this.classList.contains('jellyseerr-section') && this.isConnected) {
                diagSend('Seerr-Bereich .remove() durch ' + caller());
            }
            return originalRemove.apply(this, arguments);
        };
        var originalRemoveChild = Node.prototype.removeChild;
        Node.prototype.removeChild = function (child) {
            if (isSeerr(child)) {
                diagSend('Seerr-Bereich removeChild aus ' + short(this) + ' durch ' + caller());
            }
            return originalRemoveChild.apply(this, arguments);
        };
        var navigation = function (kind) {
            if (/^#\/search/.test(window.location.hash || '') || document.querySelector('.jellyseerr-section')) {
                var visibleInput = document.querySelector('#searchPage:not(.hide) #searchTextInput');
                diagSend('Navigation ' + kind + ' ' + (window.location.hash || '').split('?')[0] + ', Suchseiten ' + document.querySelectorAll('#searchPage').length
                    + ', sichtbares Suchfeld ' + !!visibleInput + ', Seerr-Bereich ' + document.querySelectorAll('.jellyseerr-section').length);
            }
        };
        ['pushState', 'replaceState'].forEach(function (name) {
            var original = window.history[name];
            if (typeof original === 'function' && !original.lrWrapped) {
                var wrappedHistory = function () {
                    var result = original.apply(this, arguments);
                    window.setTimeout(function () {
                        navigation(name);
                    }, 250);
                    return result;
                };
                wrappedHistory.lrWrapped = true;
                window.history[name] = wrappedHistory;
            }
        });
        window.addEventListener('hashchange', function () {
            window.setTimeout(function () {
                navigation('hashchange');
            }, 250);
        });
    }

    // Nach einer Tipp-Pause auf der Suchseite: Stand von Jellyfin Enhanced und dem Jellyseerr-Bereich
    var diagSearch = { timer: null };
    document.addEventListener('input', function (e) {
        var target = e.target;
        if (!diagOn() || !target || !target.matches || !target.matches('#searchTextInput') || !/^#\/search/.test(window.location.hash || '')) {
            return;
        }
        window.clearTimeout(diagSearch.timer);
        diagSearch.timer = window.setTimeout(function () {
            var je = window.JellyfinEnhanced || window.JE;
            var config = je && je.pluginConfig;
            var sections = document.querySelectorAll('.jellyseerr-section');
            var visible = [].filter.call(sections, function (n) {
                return !!n.offsetParent;
            });
            diagSend('Suche (' + (target.value || '').length + ' Zeichen): Enhanced ' + (je ? 'geladen' : 'NICHT geladen')
                + (config ? ', Jellyseerr ' + !!config.JellyseerrEnabled + ', Suchtreffer ' + (config.JellyseerrShowSearchResults !== false) : ', keine Plugin-Einstellungen')
                + ', Suchseiten ' + document.querySelectorAll('#searchPage').length
                + ', Seerr-Bereich ' + sections.length + ' (sichtbar ' + visible.length + ', Karten '
                + (visible[0] ? visible[0].querySelectorAll('.card').length : 0) + ')');
        }, 4000);
    }, true);

    window.addEventListener('error', function (e) {
        var where = e.filename ? ' @ ' + e.filename.split('/').pop().split('?')[0] + ':' + e.lineno + ':' + e.colno : '';
        diagStep('Fehler: ' + e.message + where);
        diagSend('Fehler: ' + e.message + where);
    });
    window.addEventListener('unhandledrejection', function (e) {
        var reason = e.reason;
        var text = reason && reason.message ? reason.message : String(reason);
        if (reason && reason.status) {
            text = 'HTTP ' + reason.status + ' ' + (reason.url || '');
        }
        diagStep('Promise abgelehnt: ' + text);
        diagSend('Promise abgelehnt: ' + text);
    });
    // Sauber verlassen (Neuladen, Wechsel) – bei einem Absturz des WebViews kommt kein pagehide
    // Jellyfin schickt beim Ansichtswechsel eigene, hochblubbernde pagehide/pageshow-Ereignisse – nur die des Fensters zählen
    window.addEventListener('pagehide', function (e) {
        if (e.target !== window && e.target !== document) {
            return;
        }
        diag.clean = true;
        diagStep('pagehide');
    });
    window.addEventListener('pageshow', function (e) {
        if (e.persisted && (e.target === window || e.target === document)) {
            diag.clean = false;
            diagStep('pageshow aus dem Zwischenspeicher');
        }
    });
    document.addEventListener('visibilitychange', function () {
        diagStep('Sichtbarkeit: ' + document.visibilityState);
    });

    // Langer Text auf wenige Zeilen gekürzt, mit „Mehr“ zum Aufklappen (z. B. Videobeschreibungen voller Links)
    function clampedText(tag, className, text) {
        var box = el('div', 'lr-clamp-box');
        var node = el(tag, className + ' lr-clamp', text);
        box.appendChild(node);
        var more = el('button', 'lr-clamp-more', 'Mehr');
        more.type = 'button';
        more.hidden = true;
        more.addEventListener('click', function (e) {
            e.preventDefault();
            e.stopPropagation();
            var open = node.classList.toggle('lr-clamp-open');
            more.textContent = open ? 'Weniger' : 'Mehr';
        });
        box.appendChild(more);
        // Erst nach dem Einhängen messbar
        window.requestAnimationFrame(function () {
            window.requestAnimationFrame(function () {
                more.hidden = node.scrollHeight <= node.clientHeight + 2;
            });
        });
        return box;
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
            PrimaryImageAspectRatio: item.PrimaryImageAspectRatio,
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
        if (item.Type === 'UpcomingMovie') {
            return item.LrPoster || item.LrImage;
        }
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

    // Datumsnummern (YouTube-Kanäle u. Ä.: Staffel = Jahr, Folge = 101799) sind keine echten Folgennummern
    function datedNumbering(item) {
        return item.ParentIndexNumber > 1900 || item.IndexNumber > 9999;
    }

    function subtitle(item, showReason) {
        if (item.Type === 'Episode') {
            if (datedNumbering(item)) {
                return item.Name || (item.PremiereDate ? new Date(item.PremiereDate).toLocaleDateString('de-DE') : '');
            }
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
        if (item.MomentReason) {
            return item.MomentReason;
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

    // ---------------------------------------------------------------- Live-TV-Erinnerungen (Kalender)

    var reminders = { ids: {}, series: {}, loaded: false };

    function loadReminders() {
        return Promise.all([getJson('LocalRecs/Reminders'), getJson('LocalRecs/Reminders/Series')]).then(function (results) {
            reminders.ids = {};
            reminders.series = {};
            (results[0] || []).forEach(function (entry) {
                reminders.ids[entry.ProgramId] = entry;
            });
            (results[1] || []).forEach(function (series) {
                reminders.series[series.Key] = series;
            });
            reminders.loaded = true;
            return results;
        }).catch(function () {
            return null;
        });
    }

    function programKey(id) {
        return String(id || '').replace(/-/g, '');
    }

    function isRemembered(item) {
        return !!reminders.ids[programKey(item.Id)];
    }

    function toggleReminder(item, onDone) {
        var id = programKey(item.Id);
        var on = !isRemembered(item);
        (on ? post('LocalRecs/Reminders/' + id) : del('LocalRecs/Reminders/' + id)).then(function () {
            if (on) {
                reminders.ids[id] = { ProgramId: id };
            } else {
                delete reminders.ids[id];
            }
            // Alle Glocken dieser Sendung (Kachel, Dialog, Menü) gleich anzeigen
            document.querySelectorAll('.lr-reminder[data-program="' + id + '"]').forEach(function (other) {
                other.classList.toggle('lr-on', on);
                var otherIcon = other.querySelector('.lr-icon');
                if (otherIcon) {
                    otherIcon.textContent = on ? 'notifications_active' : 'notifications_none';
                }
                var otherText = other.querySelector('span:not(.lr-icon)');
                if (otherText) {
                    otherText.textContent = on ? 'Gemerkt' : 'Merken';
                }
            });
            if (onDone) {
                onDone(on);
            }
        }).catch(function () {
            if (onDone) {
                onDone(!on);
            }
        });
    }

    function reminderButton(item, withLabel) {
        var b = button('lr-round lr-reminder' + (withLabel ? ' lr-reminder-label' : ''), isRemembered(item) ? 'notifications_active' : 'notifications_none',
            withLabel ? (isRemembered(item) ? 'Gemerkt' : 'Merken') : null, function () {
                toggleReminder(item, function (on) {
                    b.querySelector('.lr-icon').textContent = on ? 'notifications_active' : 'notifications_none';
                    b.classList.toggle('lr-on', on);
                    var text = b.querySelector('span:not(.lr-icon)');
                    if (text) {
                        text.textContent = on ? 'Gemerkt' : 'Merken';
                    }
                });
            });
        b.classList.toggle('lr-on', isRemembered(item));
        b.setAttribute('data-program', programKey(item.Id));
        b.title = 'An diese Sendung erinnern (Kalender)';
        return b;
    }

    function seriesReminderButton(item) {
        var seriesButton = button('lr-btn-link', 'repeat', 'Serie merken', function () {
            post('LocalRecs/Reminders/Series/' + programKey(item.Id)).then(function () {
                seriesButton.querySelector('span:not(.lr-icon)').textContent = 'Serie gemerkt';
                seriesButton.classList.add('lr-on');
            });
        });
        seriesButton.title = 'An alle künftigen Ausstrahlungen erinnern';
        return seriesButton;
    }

    // ---------------------------------------------------------------- Aufnehmen (wie Jellyfin: Vorgaben aus LiveTv/Timers/Defaults)

    var recordRights = { user: null, promise: null };

    function canRecord() {
        if (recordRights.user !== userId() || !recordRights.promise) {
            recordRights.user = userId();
            recordRights.promise = api().getCurrentUser().then(function (user) {
                return !!(user && user.Policy && (user.Policy.EnableLiveTvManagement || user.Policy.IsAdministrator));
            }).catch(function () {
                return false;
            });
        }
        return recordRights.promise;
    }

    // „Aufnehmen“ bzw. „Serie aufnehmen“ mit Status; compact = runder Knopf ohne Text (Vorschaukarte)
    function recordButton(program, series, compact) {
        var b = button(compact ? 'lr-round lr-record' : 'lr-btn-link lr-record', 'fiber_manual_record', compact ? null : 'Aufnehmen', function () {
            var timerId = series ? program.SeriesTimerId : program.TimerId;
            var request;
            if (timerId) {
                if (!window.confirm(series ? 'Serienaufnahme von „' + program.Name + '“ beenden?' : 'Aufnahme von „' + program.Name + '“ abbrechen?')) {
                    return;
                }
                request = del('LiveTv/' + (series ? 'SeriesTimers/' : 'Timers/') + timerId);
            } else {
                request = getJson('LiveTv/Timers/Defaults?programId=' + programKey(program.Id)).then(function (defaults) {
                    return postJson(series ? 'LiveTv/SeriesTimers' : 'LiveTv/Timers', defaults);
                });
            }
            b.disabled = true;
            request.then(function () {
                // Jellyfin legt Timer etwas verzögert an – Status kurz danach neu holen
                return new Promise(function (resolve) {
                    window.setTimeout(resolve, 600);
                });
            }).then(function () {
                return loadProgram(program.Id);
            }).then(function (fresh) {
                program.TimerId = fresh.TimerId;
                program.SeriesTimerId = fresh.SeriesTimerId;
                document.querySelectorAll('.lr-record[data-program="' + programKey(program.Id) + '"]').forEach(function (other) {
                    if (other.lrUpdate) {
                        other.lrUpdate();
                    }
                });
            }).catch(function () {
                window.alert('Die Aufnahme konnte nicht geändert werden.');
            }).then(function () {
                b.disabled = false;
            });
        });
        b.setAttribute('data-program', programKey(program.Id));
        b.lrUpdate = function () {
            var on = !!(series ? program.SeriesTimerId : program.TimerId);
            b.classList.toggle('lr-on', on);
            var text = series ? (on ? 'Serie wird aufgenommen ✓' : 'Serie aufnehmen') : (on ? 'Aufnahme geplant ✓' : 'Aufnehmen');
            var label = b.querySelector('span:not(.lr-icon)');
            if (label) {
                label.textContent = text;
            }
            b.title = on ? (series ? 'Serienaufnahme beenden' : 'Aufnahme abbrechen') : text;
        };
        b.lrUpdate();
        return b;
    }

    // Hängt die Aufnahme-Knöpfe an, sobald Recht und aktueller Timer-Status bekannt sind
    function addRecordButtons(item, container, compact, before) {
        if (item.Type !== 'Program' || (item.EndDate && Date.parse(item.EndDate) < Date.now())) {
            return;
        }
        canRecord().then(function (ok) {
            return ok ? loadProgram(item.Id) : null;
        }).then(function (program) {
            if (!program || !document.body.contains(container)) {
                return;
            }
            var buttons = [recordButton(program, false, compact)];
            if (program.IsSeries && !compact) {
                buttons.push(recordButton(program, true, false));
            }
            buttons.forEach(function (b) {
                container.insertBefore(b, before && before.parentNode === container ? before : null);
            });
        }).catch(function () {
            return null;
        });
    }

    function isUpcoming(start) {
        return !!start && Date.parse(start) > Date.now();
    }

    // ---------------------------------------------------------------- Glocke auch in Jellyfins eigenen Live-TV-Ansichten

    // Zuletzt angeklickte Sendung (Kachel, ⋮-Knopf oder Zelle im Fernsehprogramm) – für Kontextmenü und Aufnahme-Dialog
    var lastProgram = { id: null, time: 0 };

    function rememberProgramTarget(e) {
        var node = e.target && e.target.closest ? e.target.closest('[data-id][data-type="Program"]') : null;
        if (node && !node.closest('.lr-home-root, .lr-overlay')) {
            lastProgram = { id: node.getAttribute('data-id'), time: Date.now() };
        }
    }

    document.addEventListener('pointerdown', rememberProgramTarget, true);
    document.addEventListener('click', rememberProgramTarget, true);
    document.addEventListener('contextmenu', rememberProgramTarget, true);

    function remindersOn() {
        return !!(state.config && state.config.Reminders && userId());
    }

    function loadProgram(id) {
        return getJson('LiveTv/Programs/' + id + '?userId=' + userId());
    }

    // Kontextmenü (Aktionsblatt) einer künftigen Sendung: „An Sendung erinnern“ und „An Serie erinnern“
    function extendActionSheet(sheet) {
        if (!remindersOn() || !lastProgram.id || Date.now() - lastProgram.time > 3000 || sheet.querySelector('.lr-sheet-item')) {
            return;
        }
        var scroller = sheet.querySelector('.actionSheetScroller');
        if (!scroller || !scroller.querySelector('.actionSheetMenuItem')) {
            return;
        }
        var id = lastProgram.id;
        Promise.all([loadProgram(id), reminders.loaded ? null : loadReminders()]).then(function (results) {
            var item = results[0];
            if (!item || !isUpcoming(item.StartDate) || !document.body.contains(sheet)) {
                return;
            }
            var sheetItem = function (iconName, text, onClick) {
                var b = el('button', 'listItem listItem-button lr-sheet-item');
                b.type = 'button';
                var i = el('span', 'actionsheetMenuItemIcon listItemIcon listItemIcon-transparent material-icons ' + iconName);
                i.setAttribute('aria-hidden', 'true');
                b.appendChild(i);
                var bodyBox = el('div', 'listItemBody actionsheetListItemBody');
                var label = el('div', 'listItemBodyText actionSheetItemText', text);
                bodyBox.appendChild(label);
                b.appendChild(bodyBox);
                b.addEventListener('click', function (e) {
                    e.preventDefault();
                    e.stopPropagation();
                    onClick(b, label, i);
                });
                return b;
            };
            var single = sheetItem(isRemembered(item) ? 'notifications_active' : 'notifications_none',
                isRemembered(item) ? 'Erinnerung entfernen' : 'An Sendung erinnern', function (b, label, i) {
                    toggleReminder(item, function (on) {
                        i.className = i.className.replace(/notifications_\w+/, on ? 'notifications_active' : 'notifications_none');
                        label.textContent = on ? 'Erinnerung gesetzt ✓' : 'Erinnerung entfernt';
                    });
                });
            var series = sheetItem('repeat', 'An Serie erinnern', function (b, label) {
                post('LocalRecs/Reminders/Series/' + programKey(item.Id)).then(function () {
                    label.textContent = 'Serie gemerkt ✓';
                });
            });
            scroller.insertBefore(series, scroller.firstChild);
            scroller.insertBefore(single, scroller.firstChild);
        }).catch(function () {
            return null;
        });
    }

    // Dialog einer Sendung im Fernsehprogramm (Aufnahme-Dialog von Jellyfin): Glocke und „Serie merken“
    function extendRecordingDialog(dialog) {
        if (!remindersOn() || !lastProgram.id || Date.now() - lastProgram.time > 10000 || dialog.querySelector('.lr-rec-reminders')) {
            return;
        }
        var id = lastProgram.id;
        Promise.all([loadProgram(id), reminders.loaded ? null : loadReminders()]).then(function (results) {
            var item = results[0];
            var target = dialog.querySelector('.recordingDetails');
            if (!item || !target || !isUpcoming(item.StartDate) || dialog.querySelector('.lr-rec-reminders')) {
                return;
            }
            var box = el('div', 'lr-rec-reminders');
            box.appendChild(reminderButton(item, true));
            box.appendChild(seriesReminderButton(item));
            var fields = target.querySelector('.recordingFields');
            target.insertBefore(box, fields ? fields.nextSibling : null);
        }).catch(function () {
            return null;
        });
    }

    // Kacheln künftiger Sendungen in Jellyfins Live-TV-Reihen: kleine Glocke oben rechts
    function addCardBells(root) {
        if (!remindersOn()) {
            return;
        }
        (root || document).querySelectorAll('.card[data-type="Program"][data-startdate]:not(.lr-belled)').forEach(function (card) {
            card.classList.add('lr-belled');
            if (card.closest('.lr-home-root, .lr-overlay') || card.querySelector('.lr-native-bell') || !isUpcoming(card.getAttribute('data-startdate'))) {
                return;
            }
            var box = card.querySelector('.cardScalable') || card.querySelector('.cardBox');
            if (!box) {
                return;
            }
            var item = { Id: card.getAttribute('data-id'), StartDate: card.getAttribute('data-startdate') };
            var bell = reminderButton(item, false);
            bell.classList.add('lr-card-bell', 'lr-native-bell');
            bell.addEventListener('click', function (e) {
                e.stopPropagation();
            }, true);
            box.appendChild(bell);
        });
    }

    var bellObserverTimer = null;
    new MutationObserver(function (mutations) {
        mutations.forEach(function (m) {
            m.addedNodes.forEach(function (node) {
                if (node.nodeType !== 1) {
                    return;
                }
                var sheet = node.classList.contains('actionSheet') ? node : node.querySelector && node.querySelector('.actionSheet');
                if (sheet) {
                    extendActionSheet(sheet);
                }
                var recording = node.classList.contains('recordingDialog') ? node : node.querySelector && node.querySelector('.recordingDialog');
                if (recording) {
                    // Inhalt kommt erst nach dem Laden der Sendung
                    var tries = 0;
                    var wait = window.setInterval(function () {
                        tries++;
                        if (recording.querySelector('.recordingDetails') || tries > 40) {
                            window.clearInterval(wait);
                            extendRecordingDialog(recording);
                        }
                    }, 100);
                }
            });
        });
        if (remindersOn() && document.querySelector('.card[data-type="Program"][data-startdate]:not(.lr-belled)')) {
            window.clearTimeout(bellObserverTimer);
            bellObserverTimer = window.setTimeout(function () {
                if (!reminders.loaded) {
                    loadReminders().then(function () {
                        addCardBells();
                    });
                } else {
                    addCardBells();
                }
            }, 300);
        }
    }).observe(document.documentElement, { childList: true, subtree: true });

    // Programm-Reihen von Jellyfin (Serien, Filme, Sport …) zeigen sonst auch laufende Sendungen mit Startzeit von heute früh –
    // dort nur künftige Sendungen nach Beginn sortiert; laufende stehen in „Gerade läuft“
    // Gilt für die alte Seite (ApiClient, fetch) und die neue React-Seite (SDK, XMLHttpRequest) – beide fragen LiveTv/Programs ab
    function upcomingProgramsUrl(requestUrl) {
        if (typeof requestUrl !== 'string' || !/\/LiveTv\/Programs\?/i.test(requestUrl)) {
            return requestUrl;
        }
        var query = requestUrl.slice(requestUrl.indexOf('?') + 1);
        if (!/(^|&)hasAired=false(&|$)/i.test(query) || /(^|&)(isAiring|minStartDate|maxStartDate)=/i.test(query)) {
            return requestUrl;
        }
        var extra = '&minStartDate=' + encodeURIComponent(new Date().toISOString());
        if (!/(^|&)sortBy=/i.test(query)) {
            extra += '&sortBy=StartDate';
        }
        return requestUrl + extra;
    }

    function patchUpcomingPrograms() {
        if (window.lrUpcomingPatched) {
            return;
        }
        window.lrUpcomingPatched = true;
        var open = window.XMLHttpRequest && window.XMLHttpRequest.prototype.open;
        if (open) {
            window.XMLHttpRequest.prototype.open = function (method, requestUrl) {
                var args = Array.prototype.slice.call(arguments);
                args[1] = upcomingProgramsUrl(requestUrl);
                return open.apply(this, args);
            };
        }
        var originalFetch = window.fetch;
        if (originalFetch) {
            window.fetch = function (input, init) {
                if (typeof input === 'string') {
                    input = upcomingProgramsUrl(input);
                }
                return originalFetch.call(this, input, init);
            };
        }
    }

    patchUpcomingPrograms();

    function openCalendarDialog() {
        var dialog = overlayShell('lr-calendar');
        var body = el('div', 'lr-cal-body');
        body.appendChild(el('h2', 'lr-cal-title', 'Erinnerungen im Kalender'));
        body.appendChild(el('p', 'lr-cal-text', 'Gemerkte Sendungen erscheinen in deinem Kalender – mit Erinnerung kurz vor Beginn. '
            + 'Einmal abonnieren, dann aktualisiert sich der Kalender von selbst.'));
        var links = el('div', 'lr-cal-links');
        body.appendChild(links);
        var listBox = el('div', 'lr-cal-list');
        body.appendChild(listBox);
        dialog.appendChild(body);

        var renderLinks = function (link) {
            links.innerHTML = '';
            var actions = el('div', 'lr-cal-actions');
            var subscribe = el('a', 'lr-btn lr-btn-play', null);
            // In Apps (z. B. der iOS-App) öffnen webcal-Links nichts – dort die Abo-Seite in Safari öffnen
            var inApp = /iPhone|iPad|iPod/.test(navigator.userAgent) || !!window.NativeShell;
            subscribe.href = inApp && link.PageUrl ? link.PageUrl : link.WebcalUrl;
            if (inApp && link.PageUrl) {
                subscribe.target = '_blank';
                subscribe.rel = 'noopener';
            }
            subscribe.appendChild(icon('event'));
            subscribe.appendChild(el('span', null, 'Im Kalender abonnieren'));
            actions.appendChild(subscribe);
            actions.appendChild(button('lr-btn-info', 'content_copy', 'Link kopieren', function () {
                var done = function () {
                    copyText.textContent = 'Kopiert';
                };
                if (navigator.clipboard && navigator.clipboard.writeText) {
                    navigator.clipboard.writeText(link.Url).then(done, function () {
                        window.prompt('Link kopieren:', link.Url);
                    });
                } else {
                    window.prompt('Link kopieren:', link.Url);
                }
            }));
            var copyText = el('span', 'lr-cal-copied');
            actions.appendChild(copyText);
            links.appendChild(actions);
            var qr = el('img', 'lr-cal-qr');
            qr.alt = 'QR-Code zum Abonnieren';
            qr.src = url('LocalRecs/Calendar/Qr?ApiKey=' + api().accessToken() + '&v=' + Date.now());
            var qrBox = el('div', 'lr-cal-qrbox');
            qrBox.appendChild(qr);
            qrBox.appendChild(el('p', 'lr-cal-hint', 'Mit der Handy-Kamera scannen – es öffnet sich eine Seite mit dem Knopf „Im Kalender abonnieren“. '
                + 'Android/Google Kalender: Link kopieren und am PC unter „Weitere Kalender → Per URL“ einfügen.'));
            links.appendChild(qrBox);
            links.appendChild(button('lr-btn-link', 'autorenew', 'Link erneuern (alter Link wird ungültig)', function () {
                if (window.confirm('Neuen Link erzeugen? Bestehende Kalender-Abos hören dann auf, sich zu aktualisieren.')) {
                    post('LocalRecs/Calendar/Renew').then(function () {
                        return getJson('LocalRecs/Calendar');
                    }).then(renderLinks);
                }
            }));
        };

        var renderList = function () {
            listBox.innerHTML = '';
            loadReminders().then(function (results) {
                listBox.innerHTML = '';
                var entries = results ? results[0] || [] : [];
                var series = results ? results[1] || [] : [];
                listBox.appendChild(el('h3', 'lr-info-heading', 'Gemerkt'));
                if (!entries.length && !series.length) {
                    listBox.appendChild(el('p', 'lr-cal-text', 'Noch nichts gemerkt. Tippe bei einer Sendung auf die Glocke.'));
                }
                series.forEach(function (s) {
                    var row = el('div', 'lr-cal-row');
                    row.appendChild(icon('repeat'));
                    row.appendChild(el('div', 'lr-cal-row-text', 'Serie: ' + s.Name));
                    row.appendChild(button('lr-round', 'close', null, function () {
                        del('LocalRecs/Reminders/Series/' + encodeURIComponent(s.Key)).then(renderList);
                    }));
                    listBox.appendChild(row);
                });
                entries.forEach(function (entry) {
                    var row = el('div', 'lr-cal-row');
                    var start = new Date(entry.StartDate);
                    row.appendChild(el('div', 'lr-cal-when', start.toLocaleDateString('de-DE', { weekday: 'short', day: '2-digit', month: '2-digit' }) + ' ' + timeText(entry.StartDate)));
                    row.appendChild(el('div', 'lr-cal-row-text', (entry.ChannelName ? entry.ChannelName + ': ' : '') + entry.Name + (entry.EpisodeTitle ? ' – ' + entry.EpisodeTitle : '')));
                    if (!entry.IsSeries) {
                        row.appendChild(button('lr-round', 'close', null, function () {
                            del('LocalRecs/Reminders/' + entry.ProgramId).then(renderList);
                        }));
                    } else {
                        row.appendChild(el('span', 'lr-cal-badge', 'Serie'));
                    }
                    listBox.appendChild(row);
                });
            });
        };

        getJson('LocalRecs/Calendar').then(renderLinks).catch(function () {
            links.appendChild(el('p', 'lr-cal-text', 'Der Kalender ist gerade nicht erreichbar.'));
        });
        renderList();
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
    // report: Gründe für „keine Sitzung“ in die Diagnose (nur beim Abspielen, nicht beim Abgleich im Player)
    function ownSession(report) {
        var client = api();
        var deviceId = client.deviceId ? client.deviceId() : null;
        if (!deviceId) {
            if (report) {
                diagStep('Keine Geräte-ID im ApiClient', false, true);
            }
            return Promise.resolve(null);
        }
        return getJson('Sessions?deviceId=' + encodeURIComponent(deviceId)).then(function (sessions) {
            var found = (sessions || []).filter(function (s) {
                return s.SupportsRemoteControl !== false;
            })[0] || null;
            if (!found && report) {
                diagStep('Eigene Sitzung nicht gefunden: ' + (sessions || []).length + ' Sitzung(en) für dieses Gerät, Fernsteuerung '
                    + (sessions || []).map(function (s) {
                        return s.SupportsRemoteControl + '/' + s.Client;
                    }).join(', '), false, true);
            }
            return found;
        }).catch(function (err) {
            if (report) {
                diagStep('Sitzungen nicht abrufbar: ' + errorText(err), false, true);
            }
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

    // Abspielen direkt mit Jellyfins playbackManager (wie dessen eigene Knöpfe). Jellyfin Web stellt ihn nicht global
    // bereit – er wird einmal aus den Webpack-Modulen gesucht (das bereits geladene Modul mit isPlayingVideo/_currentPlayer).
    // Ersatzwege: PlayNow über die eigene Sitzung, zuletzt Jellyfins Abspielknopf auf der Detailseite.
    var playState = { osdAt: 0, manager: null, searches: 0 };

    function playbackManager() {
        if (playState.manager || playState.searches >= 3) {
            return playState.manager;
        }
        playState.searches++;
        try {
            var chunks = window.webpackChunk;
            var req = null;
            if (!chunks || typeof chunks.push !== 'function') {
                return null;
            }
            chunks.push([['lr-pm-' + playState.searches + '-' + Date.now()], {}, function (r) {
                req = r;
            }]);
            if (!req || !req.m) {
                return null;
            }
            Object.keys(req.m).some(function (key) {
                var source = Function.prototype.toString.call(req.m[key]);
                if (source.indexOf('isPlayingVideo=function') < 0 || source.indexOf('_currentPlayer') < 0) {
                    return false;
                }
                var exported = req(key);
                Object.keys(exported || {}).some(function (name) {
                    var candidate = exported[name];
                    if (candidate && typeof candidate.play === 'function' && typeof candidate.isPlayingVideo === 'function'
                        && typeof candidate.getCurrentPlayer === 'function') {
                        playState.manager = candidate;
                        return true;
                    }
                    return false;
                });
                return !!playState.manager;
            });
        } catch (e) {
            diagStep('playbackManager nicht gefunden: ' + errorText(e), false, true);
        }
        return playState.manager;
    }

    // Spielt ein Element sofort ab; Promise endet, wenn der Start angestoßen ist
    function playNow(target, ticks) {
        var manager = playbackManager();
        if (manager) {
            diagStep('Direktstart über Jellyfins playbackManager: ' + target.Type + ' ' + target.Id, false, true);
            return Promise.resolve(manager.play({ ids: [target.Id], serverId: api().serverId(), startPositionTicks: ticks || 0 }))
                .catch(function (err) {
                    diagStep('playbackManager.play fehlgeschlagen: ' + errorText(err) + ' – über die Sitzung', false, true);
                    return playViaSession(target, ticks);
                });
        }
        diagStep('playbackManager nicht verfügbar – über die Sitzung', false, true);
        return playViaSession(target, ticks);
    }

    function play(item) {
        diagStep('Abspielen: ' + item.Type + ' ' + item.Id, false, true);
        resolvePlayable(item).then(function (target) {
            if (!target) {
                diagStep('Nichts Abspielbares gefunden – Detailseite', false, true);
                openDetailsPage(item);
                return null;
            }
            closeOverlays();
            stopHeroVideo();
            var ticks = target.UserData && target.UserData.PlaybackPositionTicks ? target.UserData.PlaybackPositionTicks : 0;
            return playNow(target, ticks);
        }).catch(function (err) {
            diagStep('Abspielen fehlgeschlagen: ' + errorText(err) + ' – lokaler Start', false, true);
            playLocally(item);
        });
    }

    function playViaSession(target, ticks) {
        return ownSession(true).then(function (session) {
            if (!session) {
                diagStep('Keine fernsteuerbare eigene Sitzung – lokaler Start', false, true);
                playLocally(target);
                return null;
            }
            var client = api();
            var socket = client && typeof client.isWebSocketOpen === 'function' ? String(client.isWebSocketOpen()) : '?';
            diagStep('PlayNow an eigene Sitzung ' + session.Id.slice(0, 8) + ': ' + target.Type + ' ' + target.Id + ' (Fernsteuerung '
                + session.SupportsRemoteControl + ', WebSocket offen ' + socket + ')', false, true);
            var sentAt = Date.now();
            return post('Sessions/' + session.Id + '/Playing?playCommand=PlayNow&itemIds=' + target.Id
                + (ticks ? '&startPositionTicks=' + ticks : '')).then(function () {
                diagStep('PlayNow angenommen', false, true);
                window.setTimeout(function () {
                    checkPlaybackStarted(target, sentAt);
                }, 6000);
            });
        });
    }

    // Hat der Browser seit dem Befehl Wiedergabe-Infos geholt, ist der Start unterwegs (auch wenn er noch lädt oder scheitert)
    function playbackRequestedSince(sentAt) {
        try {
            var origin = window.performance.timeOrigin || window.performance.timing.navigationStart;
            return window.performance.getEntriesByType('resource').some(function (entry) {
                return /\/PlaybackInfo/.test(entry.name) && origin + entry.startTime >= sentAt;
            });
        } catch (e) {
            return false;
        }
    }

    function checkPlaybackStarted(target, sentAt) {
        if (playState.osdAt >= sentAt || document.querySelector('#videoOsdPage:not(.hide)')) {
            diagStep('Wiedergabe nach PlayNow gestartet', false, true);
            return;
        }
        if (playbackRequestedSince(sentAt)) {
            diagStep('Nach PlayNow Wiedergabe-Infos geholt, aber kein Player sichtbar – kein zweiter Start', false, true);
            return;
        }
        ownSession(true).then(function (session) {
            if (session && session.NowPlayingItem) {
                diagStep('Wiedergabe läuft laut Server (' + session.NowPlayingItem.Id + ')', false, true);
                return;
            }
            diagStep('6 s nach PlayNow keine Wiedergabe – lokaler Start', false, true);
            playLocally(target);
        });
    }

    // Jellyfins Abspielknopf (Fortsetzen bzw. Abspielen) auf der Detailseite – spielt im Browser selbst, ohne Server-Umweg
    function nativePlayButton() {
        var match = detailIdFromHash();
        var view = document.querySelector('#itemDetailPage:not(.hide)');
        var buttonNode = view && view.querySelector('.mainDetailButtons .btnPlay:not(.hide)');
        return buttonNode ? { id: match, button: buttonNode } : null;
    }

    function playLocally(target) {
        var current = nativePlayButton();
        if (current && current.id === target.Id) {
            diagStep('Lokaler Start über Jellyfins Abspielknopf', false, true);
            current.button.click();
            return;
        }
        try {
            window.sessionStorage.setItem('lrWeb.autoPlay', target.Id);
        } catch (e) {
            // ohne Merker öffnet sich nur die Detailseite
        }
        diagStep('Lokaler Start: Detailseite öffnen, dann Abspielknopf', false, true);
        openDetailsPage(target);
        waitForAutoPlay();
    }

    function waitForAutoPlay() {
        var tries = 0;
        var timer = window.setInterval(function () {
            tries++;
            var wanted = null;
            try {
                wanted = window.sessionStorage.getItem('lrWeb.autoPlay');
            } catch (e) {
                wanted = null;
            }
            if (!wanted || tries > 50) {
                window.clearInterval(timer);
                if (wanted) {
                    diagStep('Abspielknopf nicht gefunden', false, true);
                }
                return;
            }
            var current = nativePlayButton();
            if (current && current.id === wanted) {
                window.clearInterval(timer);
                try {
                    window.sessionStorage.removeItem('lrWeb.autoPlay');
                } catch (e) {
                    // egal
                }
                diagStep('Lokaler Start über Jellyfins Abspielknopf', false, true);
                current.button.click();
            }
        }, 200);
    }

    // ---------------------------------------------------------------- Karten und Reihen

    function onActivate(node, handler) {
        node.tabIndex = 0;
        function run(e) {
            diagStep('Tippen: ' + String(node.className).split(' ').slice(0, 2).join('.'));
            try {
                handler(e);
            } catch (err) {
                diagStep('Fehler beim Tippen: ' + errorText(err), false, true);
                throw err;
            }
        }
        node.addEventListener('click', run);
        node.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') {
                run(e);
            }
        });
    }

    function itemClick(item) {
        if (item.LrUpcoming) {
            openUpcoming(item);
            return;
        }
        if (item.Type === 'Program' || item.Type === 'TvChannel') {
            if (item.Type === 'TvChannel' || isAiring(item)) {
                play(item);
            } else {
                openInfo(item);
            }
            return;
        }
        openInfo(item);
    }

    // Bild für Querformat-Kacheln. "clean" = ohne eingebrannten Titel (Backdrop, Folgenbild), dann kommt Logo/Titel darüber.
    // Thumb-Bilder enthalten meist schon den Titel – die bekommen kein zweites Logo.
    function cardImage(item) {
        if (item.Type === 'UpcomingMovie') {
            return { src: item.LrImage, clean: true, poster: !!item.LrImageIsPoster };
        }
        var tags = item.ImageTags || {};
        var backdrop = item.BackdropImageTags && item.BackdropImageTags.length;
        var parentBackdrop = item.ParentBackdropItemId && item.ParentBackdropImageTags && item.ParentBackdropImageTags.length;
        // Folgenbild nur im Querformat – manche Specials haben ein Hochformat-Bild
        if (item.Type === 'Episode' && tags.Primary && !(item.PrimaryImageAspectRatio && item.PrimaryImageAspectRatio < 1.2)) {
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
        // Hochformat-Bilder im Querformat-Rahmen: ganz zeigen statt abschneiden, Kartenformat bleibt gleich
        var portrait = item.PrimaryImageAspectRatio && item.PrimaryImageAspectRatio < 1.2;
        if (tags.Primary) {
            return { src: imageUrl(item.Id, 'Primary', tags.Primary, 640), clean: true, poster: !!portrait };
        }
        if (item.SeriesId && item.SeriesPrimaryImageTag) {
            return { src: imageUrl(item.SeriesId, 'Primary', item.SeriesPrimaryImageTag, 640), clean: true, poster: true };
        }
        return { src: null, clean: true };
    }

    function landscapeCard(item, showReason) {
        var card = el('div', 'lr-card lr-card-landscape');
        var image = cardImage(item);
        var frame = el('div', 'lr-card-frame');
        if (image.src) {
            var img = el('img', image.poster ? 'lr-card-img lr-card-img-poster' : 'lr-card-img');
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
            frame.appendChild(el('span', 'material-icons lr-card-play', 'play_circle_outline'));
        }
        if (item.Type === 'Program' && isAiring(item)) {
            frame.appendChild(el('div', 'lr-live', 'LIVE'));
        }
        if (item.Type === 'Program' && !isAiring(item) && item.StartDate && Date.parse(item.StartDate) > Date.now()) {
            var bell = reminderButton(item, false);
            bell.classList.add('lr-card-bell');
            frame.appendChild(bell);
        }
        card.appendChild(frame);
        var sub = subtitle(item, showReason);
        if (sub) {
            card.appendChild(el('div', 'lr-card-sub', sub));
        }
        onActivate(card, function () {
            // Angefangene Titel spielen direkt weiter (Play-Symbol in der Mitte)
            if (percent && percent > 0 && percent < 100) {
                play(item);
            } else {
                itemClick(item);
            }
        });
        attachPreview(card, item);
        return card;
    }

    // ---------------------------------------------------------------- Filmreihen

    function movieSeriesImage(series, backdrop) {
        return url('LocalRecs/MovieSeries/' + encodeURIComponent(series.Id) + '/Images/' + (backdrop ? 'Backdrop' : 'Poster'));
    }

    function movieSeriesLine(series) {
        return series.Watched ? series.Watched + ' von ' + series.Total + ' gesehen · ' + series.Reason : series.Reason;
    }

    // Karte einer Reihe: Hintergrund der Reihe (sonst des nächsten Teils), Name, „x von y gesehen · Weiter mit Teil n“
    function movieSeriesCard(series) {
        var card = el('div', 'lr-card lr-card-landscape lr-card-movieseries');
        var frame = el('div', 'lr-card-frame');
        var src = series.HasBackdrop ? movieSeriesImage(series, true) : (series.Next ? backdropImage(series.Next, 640) : null);
        if (src) {
            var img = el('img', 'lr-card-img');
            img.loading = 'lazy';
            img.alt = '';
            img.src = src;
            img.addEventListener('error', function () {
                var fallback = series.Next ? backdropImage(series.Next, 640) : null;
                if (fallback && img.src.indexOf('LocalRecs/MovieSeries') >= 0) {
                    img.src = fallback;
                } else {
                    img.remove();
                    frame.classList.add('lr-card-noimg');
                }
            });
            frame.appendChild(img);
        } else {
            frame.classList.add('lr-card-noimg');
        }
        frame.appendChild(el('div', 'lr-card-title', series.Name));
        if (series.Total) {
            var bar = el('div', 'lr-progress');
            var fill = el('div', 'lr-progress-fill');
            fill.style.width = Math.round((series.Watched || 0) * 100 / series.Total) + '%';
            bar.appendChild(fill);
            frame.appendChild(bar);
        }
        card.appendChild(frame);
        card.appendChild(el('div', 'lr-card-sub', movieSeriesLine(series)));
        onActivate(card, function () {
            openMovieSeries(series.Id);
        });
        return card;
    }

    // Alle vorhandenen Teile einer Reihe in Erscheinungsreihenfolge
    function movieSeriesGrid(detail, currentId) {
        var grid = el('div', 'lr-grid lr-movieseries-grid');
        (detail.Parts || []).forEach(function (part, i) {
            var cell = el('div', 'lr-movieseries-part' + (part.Id === currentId ? ' lr-movieseries-current' : '') + (i === detail.NextIndex ? ' lr-movieseries-next' : ''));
            cell.appendChild(el('div', 'lr-movieseries-number', 'Teil ' + (i + 1) + (part.UserData && part.UserData.Played ? ' · gesehen' : (i === detail.NextIndex ? ' · als Nächstes' : ''))));
            cell.appendChild(posterCard(part));
            grid.appendChild(cell);
        });
        return grid;
    }

    function openMovieSeries(id) {
        var dialog = overlayShell('lr-info lr-movieseries');
        dialog.appendChild(el('div', 'lr-spinner'));
        getJson('LocalRecs/MovieSeries/' + encodeURIComponent(id)).then(function (detail) {
            dialog.querySelector('.lr-spinner').remove();
            var next = detail.NextIndex >= 0 ? detail.Parts[detail.NextIndex] : null;
            var heroItem = next || detail.Parts[0];
            dialog.appendChild(heroElement(heroItem, {
                compact: true,
                noInfo: true,
                title: detail.Name,
                logo: null,
                image: detail.HasBackdrop ? movieSeriesImage(detail, true) : undefined,
                overview: null,
                meta: detail.Watched + ' von ' + detail.Total + ' gesehen',
                badge: next && detail.Watched ? 'Weiter mit Teil ' + (detail.NextIndex + 1) : null,
                actions: false
            }));
            var section = el('div', 'lr-info-section');
            if (next) {
                section.appendChild(button('lr-btn-play', 'play_arrow', (detail.Watched ? 'Weiter mit Teil ' : 'Starten mit Teil ') + (detail.NextIndex + 1), function () {
                    play(next);
                }));
            }
            section.appendChild(movieSeriesGrid(detail));
            dialog.appendChild(section);
        }).catch(function () {
            dialog.querySelector('.lr-spinner').remove();
            dialog.appendChild(el('p', 'lr-cal-text', 'Die Reihe konnte nicht geladen werden.'));
        });
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
        if (isTouch || item.LrUpcoming) {
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
        // Glocke auch in der Vorschaukarte – sie verdeckt sonst die Glocke der Kachel
        if (item.Type === 'Program' && isUpcoming(item.StartDate)) {
            actions.appendChild(reminderButton(item, false));
            if (!reminders.loaded) {
                loadReminders().then(function () {
                    var bell = actions.querySelector('.lr-reminder');
                    var on = isRemembered(item);
                    if (bell) {
                        bell.classList.toggle('lr-on', on);
                        bell.querySelector('.lr-icon').textContent = on ? 'notifications_active' : 'notifications_none';
                    }
                });
            }
        }
        if (item.Type === 'Program') {
            var recordSlot = el('span', 'lr-record-slot');
            actions.appendChild(recordSlot);
            addRecordButtons(item, recordSlot, true, null);
        }
        var more = button('lr-round lr-round-right', 'expand_more', null, function () {
            hidePreview();
            // „Weitere Infos“ zeigt immer Infos – auch bei laufenden Sendungen (nur der Play-Knopf spielt ab)
            if (item.Type === 'TvChannel') {
                openDetailsPage(item);
            } else {
                openInfo(item);
            }
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
            video.src = url('Videos/' + trailerId + '/stream?static=true&ApiKey=' + api().accessToken());
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
        // Hochformat-Karte: das textlose Hintergrundbild, auf Hochformat beschnitten. Nur ohne Hintergrundbild das Poster –
        // dann ohne Logo und Titel darüber, denn auf Postern steht der Titel fast immer schon drauf.
        var posterOnly = false;
        var image = options.image;
        if (!image && card) {
            image = backdropImage(item, 1280);
            if (!image && posterImage(item)) {
                image = posterImage(item).replace('maxWidth=360', 'maxWidth=900');
                posterOnly = true;
            }
        } else if (!image) {
            image = backdropImage(item, 1920);
        }
        if (posterOnly) {
            hero.classList.add('lr-hero-poster');
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
        var logo = options.logo !== undefined ? options.logo : logoImage(item, 800);
        if (posterOnly) {
            // Titel steht auf dem Poster
        } else if (logo) {
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
            var future = item.Type === 'Program' && !isAiring(item);
            if (!future) {
                actions.appendChild(button('lr-btn-play', 'play_arrow', item.Type === 'Program' ? 'Einschalten' : (resume ? 'Fortsetzen' : 'Abspielen'), function () {
                    play(item);
                }));
            }
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
            if (options.later) {
                // „Später“: für einige Wochen aus dem Banner, bleibt in den Empfehlungen
                var laterButton = button(card ? 'lr-btn-icon' : 'lr-btn-info', 'schedule', 'Später', options.later);
                laterButton.title = 'Für ein paar Wochen nicht mehr im Banner zeigen – bleibt in den Empfehlungen';
                actions.appendChild(laterButton);
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
        noteView(summary && summary.Id);
        var dialog = overlayShell('lr-info');
        dialog.appendChild(el('div', 'lr-spinner'));
        getJson(itemsQuery('Items/' + summary.Id).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines,ChannelInfo'))
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
        if (item.Type === 'Program') {
            var when = item.StartDate ? new Date(item.StartDate).toLocaleDateString('de-DE', { weekday: 'long', day: '2-digit', month: '2-digit' })
                + ', ' + timeText(item.StartDate) + (item.EndDate ? '–' + timeText(item.EndDate) : '') : '';
            left.appendChild(el('h2', 'lr-info-episode', [when, item.ChannelName, item.EpisodeTitle].filter(Boolean).join(' · ')));
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
        if (item.Type === 'Program' && !(item.EndDate && Date.parse(item.EndDate) < Date.now())) {
            if (!isAiring(item)) {
                actions.appendChild(reminderButton(item, true));
            }
            actions.appendChild(seriesReminderButton(item));
            if (!reminders.loaded) {
                loadReminders();
            }
            var channelButton = item.ChannelId ? button('lr-btn-link', 'live_tv', 'Sender einschalten', function () {
                play({ Id: item.ChannelId, Type: 'TvChannel', Name: item.ChannelName });
            }) : null;
            if (channelButton) {
                actions.appendChild(channelButton);
            }
            addRecordButtons(item, actions, false, channelButton);
        }
        if (isEpisode && item.SeriesId) {
            actions.appendChild(button('lr-btn-link', 'tv', 'Zur Serie', function () {
                openDetailsPage({ Id: item.SeriesId });
            }));
        }
        // Jellyfin-Detailseite mit allen Optionen (Versionen, Tonspur, Untertitel, Bearbeiten …) – bei Sendungen steht alles schon hier
        actions.appendChild(item.Type === 'Program' ? el('span') : button('lr-btn-link', 'tune', 'Alle Optionen', function () {
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
            left.appendChild(clampedText('p', 'lr-info-overview', item.Overview));
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
            // Weitere Teile der Reihe (vor „Ähnliche Titel“, Platzhalter hält die Reihenfolge)
            var seriesHolder = el('div');
            dialog.appendChild(seriesHolder);
            if (state.config && state.config.MovieSeries) {
                safe(getJson('LocalRecs/MovieSeries/ByItem/' + item.Id)).then(function (detail) {
                    if (!detail || !detail.Parts || detail.Parts.length < 2) {
                        return;
                    }
                    var section = el('div', 'lr-info-section');
                    var heading = el('h3', 'lr-info-heading', 'Weitere Teile der Reihe „' + detail.Name + '“ (' + detail.Watched + ' von ' + detail.Total + ' gesehen)');
                    section.appendChild(heading);
                    section.appendChild(movieSeriesGrid(detail, item.Id));
                    seriesHolder.appendChild(section);
                });
            }
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
        row.appendChild(el('div', 'lr-episode-number', episode.IndexNumber != null && !datedNumbering(episode) ? String(episode.IndexNumber) : ''));
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

    // ---------------------------------------------------------------- Detailseite im Quallenkino-Stil

    var DETAIL_TYPES = ['Movie', 'Series', 'Episode', 'Season', 'Program'];

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
        noteView(id);
        getJson(itemsQuery('Items/' + id).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines,ChannelInfo'))
            .then(function (item) {
                if (DETAIL_TYPES.indexOf(item.Type) < 0 || detailIdFromHash() !== id) {
                    return null;
                }
                // Staffel: Serie zeigen, mit dieser Staffel ausgewählt
                if (item.Type === 'Season' && item.SeriesId) {
                    return getJson(itemsQuery('Items/' + item.SeriesId).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',People,Studios,Taglines,ChannelInfo'))
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
            logo: world.LogoUrl ? url(world.LogoUrl) : undefined,
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
        // Filter nur für Typen, die es in der Welt gibt; nur ein Typ = keine Filter, nur die Sortierung
        var typesChecked = false;
        function showTypeChips(items) {
            var present = {
                movies: items.some(function (i) {
                    return i.Type === 'Movie';
                }),
                series: items.some(function (i) {
                    return i.Type === 'Series';
                })
            };
            if (!(present.movies && present.series)) {
                chips.remove();
            }
        }
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
                if (filter === 'all' && !typesChecked) {
                    typesChecked = true;
                    showTypeChips(items);
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
        var cfg = state.config || {};
        var tasks = [];
        tasks.push(safe(getJson('LocalRecs/Recommendations/Reasons')).then(function (reasons) {
            state.reasons = reasons || {};
        }));
        if (liveTv) {
            tasks.push(loadReminders());
        }
        tasks.push(add('resume', 'Weiterschauen für ' + user.Name, 'landscape', getJson(itemsQuery('UserItems/Resume?MediaTypes=Video&Limit=' + ROW_LIMIT))));
        tasks.push(add('nextup', 'Als Nächstes', 'landscape', getJson(itemsQuery('Shows/NextUp?Limit=' + ROW_LIMIT + '&EnableResumable=false'))));
        // Passend zur Jahreszeit (Weihnachten, Silvester, Halloween, Ostern) – nur im Zeitfenster, Titel kommt vom Server
        if (state.config && state.config.Seasonal) {
            var seasonalRow = { key: 'seasonal', title: '', kind: 'landscape', items: [] };
            rows.push(seasonalRow);
            tasks.push(safe(getJson('LocalRecs/Seasonal?utcOffsetMinutes=' + utcOffset())).then(function (result) {
                if (result && result.Active) {
                    seasonalRow.title = result.Title;
                    seasonalRow.items = itemsOf(result).map(slim);
                }
            }));
        }
        // Zeitzone des Browsers: Tageszeit für die Reihenfolge der Zeilen und tageszeit-typische Titel
        var offset = utcOffset();
        // Zeitabhängige Zeilen (Läuft heute im TV, Neu diese Woche, Kinostart, Jubiläen, Vor einem Jahr) – Titel und Begründung vom Server
        if (state.config && state.config.Moments) {
            var momentsPlaceholder = { key: 'moments', title: '', kind: 'group', items: [] };
            rows.push(momentsPlaceholder);
            tasks.push(safe(getJson('LocalRecs/Moments?utcOffsetMinutes=' + offset)).then(function (result) {
                ((result && result.Rows) || []).forEach(function (row) {
                    var reasons = row.Reasons || {};
                    momentsPlaceholder.items.push({
                        key: 'moment-' + row.Key,
                        title: row.Title,
                        kind: 'landscape',
                        items: (row.Items || []).map(function (item) {
                            var entry = slim(item);
                            entry.MomentReason = reasons[item.Id] || '';
                            return entry;
                        })
                    });
                });
            }));
        }
        // Demnächst: neue Folgen (Sonarr) und bald erscheinende Filme (Radarr) – nur lesend, nichts wird angefragt
        if (cfg.Upcoming) {
            var upcomingRow = { key: 'upcoming', title: 'Demnächst', kind: 'landscape', items: [] };
            rows.push(upcomingRow);
            tasks.push(safe(getJson('LocalRecs/Upcoming?utcOffsetMinutes=' + offset)).then(function (result) {
                upcomingRow.items = itemsOf(result).map(upcomingEntry).filter(Boolean);
            }));
        }
        tasks.push(add('movies', cfg.MoviesTitle || 'Empfohlene Filme für dich', 'landscape', getJson('LocalRecs/Recommendations?type=movies&limit=' + ROW_LIMIT + '&utcOffsetMinutes=' + offset)));
        tasks.push(add('series', cfg.SeriesTitle || 'Empfohlene Serien für dich', 'landscape', getJson('LocalRecs/Recommendations?type=series&limit=' + ROW_LIMIT + '&utcOffsetMinutes=' + offset)));
        // Banner des Tages vom Server (täglich neu, auf allen Geräten gleich, Entdecken und Weiterschauen gemischt) – keine eigene Reihe
        var bannerRow = { key: 'banner', title: '', kind: 'banner', items: [] };
        rows.push(bannerRow);
        tasks.push(safe(getJson('LocalRecs/Billboard?limit=5&utcOffsetMinutes=' + offset)).then(function (result) {
            var labels = (result && result.Reasons) || {};
            bannerRow.items = itemsOf(result).map(function (item) {
                var entry = slim(item);
                entry.BannerReason = labels[item.Id] || '';
                return entry;
            });
        }));
        var daypartOrder = null;
        tasks.push(safe(getJson('LocalRecs/Behavior/Daypart?utcOffsetMinutes=' + offset)).then(function (result) {
            daypartOrder = result && result.Order;
        }));
        if (liveTv && cfg.LiveTvRow !== false) {
            tasks.push(add('livetv', cfg.LiveTvTitle || 'Heute im TV für dich', 'landscape', getJson('LocalRecs/Recommendations?type=livetv&limit=' + ROW_LIMIT)));
        }
        // Filmreihen (TMDB-Sammlungen): angefangene mit dem nächsten Teil, dann passende
        if (state.config && state.config.MovieSeries) {
            var seriesRow = { key: 'movieseries', title: state.config.MovieSeriesTitle || 'Filmreihen für dich', kind: 'movieseries', items: [] };
            rows.push(seriesRow);
            tasks.push(safe(getJson('LocalRecs/MovieSeries?limit=12')).then(function (result) {
                seriesRow.items = ((result && result.Items) || []).map(function (s) {
                    return {
                        Id: s.Id,
                        Name: s.Name,
                        Total: s.Total,
                        Watched: s.Watched,
                        Started: s.Started,
                        Reason: s.Reason,
                        HasBackdrop: s.HasBackdrop,
                        HasPoster: s.HasPoster,
                        Next: s.Next ? slim(s.Next) : null
                    };
                });
            }));
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
            orderByDaypart(rows, daypartOrder);
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

    // Eintrag aus „Demnächst“ als Karte: Folgen zeigen die Serie aus Jellyfin, Filme Bilder aus Radarr (über den Server).
    // Die Zeile unter der Karte nennt den Termin („Neue Folge am Fr. 24.10.“, „Staffelstart heute“, „Kinostart morgen“).
    function upcomingEntry(entry) {
        var upcoming = {
            Kind: entry.Kind,
            ReleaseType: entry.ReleaseType,
            Subtitle: entry.Subtitle,
            Date: entry.Date,
            SeasonNumber: entry.SeasonNumber,
            EpisodeNumber: entry.EpisodeNumber,
            EpisodeTitle: entry.EpisodeTitle,
            EpisodeOverview: entry.EpisodeOverview ? entry.EpisodeOverview.slice(0, 600) : null,
            Network: entry.Network,
            MoreEpisodes: entry.MoreEpisodes || 0
        };
        if (entry.Series) {
            var series = slim(entry.Series);
            series.MomentReason = entry.Subtitle;
            series.LrUpcoming = upcoming;
            // Ein Klick zeigt das Kommende, nicht das Weiterspielen
            if (series.UserData) {
                series.UserData.PlayedPercentage = null;
            }
            return series;
        }
        if (!entry.Movie) {
            return null;
        }
        var movie = entry.Movie;
        return {
            Id: 'upcoming-' + movie.RadarrId,
            Type: 'UpcomingMovie',
            Name: entry.Title,
            ProductionYear: movie.Year,
            OfficialRating: movie.OfficialRating,
            Overview: movie.Overview ? movie.Overview.slice(0, 600) : null,
            MomentReason: entry.Subtitle,
            LrUpcoming: upcoming,
            LrImage: movie.BackdropUrl ? url(movie.BackdropUrl) : (movie.PosterUrl ? url(movie.PosterUrl) : null),
            LrPoster: movie.PosterUrl ? url(movie.PosterUrl) : null,
            LrImageIsPoster: !movie.BackdropUrl && !!movie.PosterUrl
        };
    }

    function pad2(n) {
        return (n < 10 ? '0' : '') + n;
    }

    // „Freitag, 24.10. · 21:00 · Paramount+“ (Filme ohne Uhrzeit)
    function upcomingWhen(upcoming) {
        var date = new Date(upcoming.Date);
        if (isNaN(date.getTime())) {
            return '';
        }
        var days = ['Sonntag', 'Montag', 'Dienstag', 'Mittwoch', 'Donnerstag', 'Freitag', 'Samstag'];
        var parts = [days[date.getDay()] + ', ' + pad2(date.getDate()) + '.' + pad2(date.getMonth() + 1) + '.' + date.getFullYear()];
        if (upcoming.Kind === 'Episode') {
            parts.push(pad2(date.getHours()) + ':' + pad2(date.getMinutes()) + ' Uhr');
            if (upcoming.Network) {
                parts.push(upcoming.Network);
            }
        }
        return parts.join(' · ');
    }

    // Was kommt: Folge (SxxEyy, Titel, Termin, Sender, Beschreibung) mit „Zur Serie“, bzw. der Film mit seinem Termin
    function openUpcoming(item) {
        var upcoming = item.LrUpcoming || {};
        var dialog = overlayShell('lr-calendar lr-upcoming');
        var body = el('div', 'lr-cal-body');
        var poster = item.Type === 'UpcomingMovie' ? item.LrPoster : posterImage(item);
        if (poster) {
            var img = el('img', 'lr-upcoming-poster');
            img.alt = '';
            img.src = poster;
            body.appendChild(img);
        }
        body.appendChild(el('h2', 'lr-cal-title', item.Name + (item.Type === 'UpcomingMovie' && item.ProductionYear ? ' (' + item.ProductionYear + ')' : '')));
        body.appendChild(el('p', 'lr-upcoming-label', upcoming.Subtitle || item.MomentReason || ''));
        if (upcoming.Kind === 'Episode') {
            var number = 'S' + pad2(upcoming.SeasonNumber || 0) + 'E' + pad2(upcoming.EpisodeNumber || 0);
            body.appendChild(el('p', 'lr-upcoming-episode', number + (upcoming.EpisodeTitle ? ' · ' + upcoming.EpisodeTitle : '')));
        }
        var when = upcomingWhen(upcoming);
        if (when) {
            body.appendChild(el('p', 'lr-cal-text lr-upcoming-when', when));
        }
        var overview = upcoming.Kind === 'Episode' ? (upcoming.EpisodeOverview || item.Overview) : item.Overview;
        if (overview) {
            body.appendChild(el('p', 'lr-cal-text', overview));
        }
        if (upcoming.MoreEpisodes > 0) {
            body.appendChild(el('p', 'lr-cal-text lr-upcoming-note', upcoming.MoreEpisodes === 1
                ? 'Danach folgt noch 1 weitere neue Folge in den nächsten Tagen.'
                : 'Danach folgen noch ' + upcoming.MoreEpisodes + ' weitere neue Folgen in den nächsten Tagen.'));
        }
        if (item.Type === 'UpcomingMovie') {
            body.appendChild(el('p', 'lr-cal-text lr-upcoming-note', 'Vorgemerkt – erscheint automatisch in der Bibliothek, sobald er verfügbar ist.'));
        } else {
            var actions = el('div', 'lr-upcoming-actions');
            actions.appendChild(button('lr-btn', 'tv', 'Zur Serie', function () {
                closeTopOverlay();
                openInfo(item);
            }));
            body.appendChild(actions);
        }
        dialog.appendChild(body);
    }

    function utcOffset() {
        return -new Date().getTimezoneOffset();
    }

    // Empfehlungszeilen (Filme, Serien, Live-TV) in der Reihenfolge, die zur Tageszeit passt – auf ihren bisherigen Plätzen
    function orderByDaypart(rows, order) {
        if (!order || !order.length) {
            return;
        }
        var keys = ['movies', 'series', 'livetv'];
        var slots = [];
        rows.forEach(function (row, index) {
            if (keys.indexOf(row.key) >= 0) {
                slots.push(index);
            }
        });
        var present = slots.map(function (index) {
            return rows[index];
        }).sort(function (a, b) {
            var ia = order.indexOf(a.key);
            var ib = order.indexOf(b.key);
            return (ia < 0 ? 99 : ia) - (ib < 0 ? 99 : ib);
        });
        slots.forEach(function (slot, i) {
            rows[slot] = present[i];
        });
    }

    // Kurzfristiges Interesse: Suchen und geöffnete Titel (der Server prüft Einstellung und Zustimmung des Benutzers)
    function behaviorOn() {
        return !!(state.config && state.config.BehaviorSignals) && !!userId();
    }

    function noteView(itemId) {
        if (behaviorOn() && itemId) {
            postJson('LocalRecs/Behavior/View', { ItemId: itemId }).catch(function () {
                // nur ein Signal
            });
        }
    }

    var searchNote = { timer: null, last: '' };
    document.addEventListener('input', function (e) {
        var target = e.target;
        if (!behaviorOn() || !target || !target.matches || !/^#\/search/.test(window.location.hash || '')
            || !target.matches('#searchTextInput, .searchfields-txtSearch, input[type="search"]')) {
            return;
        }
        window.clearTimeout(searchNote.timer);
        // Erst nach einer Tipp-Pause melden, damit nicht jeder Zwischenstand gespeichert wird
        searchNote.timer = window.setTimeout(function () {
            var term = (target.value || '').trim();
            if (term.length >= 2 && term.length <= 80 && term.toLowerCase() !== searchNote.last) {
                searchNote.last = term.toLowerCase();
                postJson('LocalRecs/Behavior/Search', { Term: term }).catch(function () {
                    // nur ein Signal
                });
            }
        }, 2500);
    }, true);

    function laterBanner(item, uid) {
        postJson('LocalRecs/Billboard/Later', { ItemId: item.Id }).then(function () {
            try {
                window.sessionStorage.removeItem('lrWeb.banner.' + uid + '.' + new Date().toDateString());
            } catch (e) {
                // dann bleibt die Auswahl bis zum Neuladen
            }
            refresh(true);
        }).catch(function () {
            // Server nicht erreichbar – Banner bleibt
        });
    }

    function pickHero(rows, uid) {
        // Banner des Tages: je Sitzung einer der Titel, am nächsten Tag neue
        var banner = (rows.filter(function (row) {
            return row.key === 'banner';
        })[0] || { items: [] }).items.filter(function (item) {
            return item.BackdropImageTags && item.BackdropImageTags.length;
        });
        if (banner.length) {
            banner.forEach(function (item) {
                item.FromBanner = true;
            });
            var today = new Date().toDateString();
            var bannerId = sessionChoice('lrWeb.banner.' + uid + '.' + today, function () {
                return banner[Math.floor(Math.random() * banner.length)].Id;
            });
            return banner.filter(function (b) {
                return b.Id === bannerId;
            })[0] || banner[0];
        }
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
        if (!row.items || !row.items.length || row.kind === 'banner') {
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
        if (row.kind === 'movieseries') {
            return rowElement(row.title, row.items.map(movieSeriesCard), 'lr-row-movieseries');
        }
        if (row.kind === 'libraries') {
            return rowElement(row.title, row.items.map(libraryCard), 'lr-row-libraries');
        }
        // Zeilen mit Termin oder Begründung bleiben auch auf dem Handy im Querformat, damit die Zeile darunter Platz hat
        if (phoneLayout() && row.key !== 'livetv' && row.key !== 'onnow' && row.key !== 'upcoming' && row.key.indexOf('moment-') !== 0) {
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
            heroHolder.appendChild(heroElement(hero, {
                badge: hero.BannerReason || state.reasons[hero.Id],
                card: true,
                overview: phoneLayout() ? null : undefined,
                later: hero.FromBanner ? function () {
                    laterBanner(hero, uid);
                } : null
            }));
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
            footer.appendChild(button('lr-btn-link', 'dashboard', 'Jellyfin-Startseite anzeigen', function () {
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
        bar.appendChild(button('lr-btn-link', 'movie_filter', 'Startseite im Quallenkino-Stil anzeigen', function () {
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

    // ---------------------------------------------------------------- Kopfleiste aufräumen (Quallenkino-Stil)
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

    // Selten gebrauchte Funktionen von Jellyfin – im Profilmenü unter „Mehr …“
    var RARE_MENU_LABELS = /synchron|syncplay|auf ger[äa]t|wiedergeben auf|cast|play on|quick ?connect/i;

    function closeUserMenu() {
        var backdrop = document.querySelector('#app-user-menu .MuiBackdrop-root');
        if (backdrop) {
            backdrop.click();
        }
    }

    // Eintrag, der einen weggeräumten Knopf oder einen versteckten Jellyfin-Eintrag auslöst
    function proxyMenuItem(node, label) {
        var item = accountMenuItem(null, label, function () {
            node.click();
        });
        var iconBox = item.querySelector('.lr-menu-icon');
        if (node.id === 'randomItemButton') {
            // Würfel von Jellyfin Enhanced wird per CSS gezeichnet – als Kopie bliebe nur ein weißes Quadrat
            iconBox.appendChild(icon('casino'));
        } else {
            var symbol = node.querySelector('svg, img, .material-icons, i');
            if (symbol) {
                iconBox.appendChild(symbol.cloneNode(true));
            }
        }
        return item;
    }

    // Aufklappbare Gruppe im Profilmenü („Quallenkino“, „Mehr …“); bei nur einem Eintrag steht dieser direkt da
    function menuGroup(key, iconName, label, children) {
        if (children.length < 2) {
            return children;
        }
        var header = el('li', 'lr-menu-item lr-menu-account lr-menu-group MuiButtonBase-root MuiMenuItem-root');
        header.setAttribute('role', 'menuitem');
        header.setAttribute('aria-expanded', 'false');
        header.dataset.group = key;
        var headerIcon = el('span', 'lr-menu-icon');
        headerIcon.appendChild(icon(iconName));
        header.appendChild(headerIcon);
        header.appendChild(el('span', 'lr-menu-group-label', label));
        header.appendChild(icon('expand_more'));
        children.forEach(function (child) {
            child.classList.add('lr-menu-sub');
            child.hidden = true;
        });
        onActivate(header, function (e) {
            e.stopPropagation();
            var open = header.getAttribute('aria-expanded') !== 'true';
            header.setAttribute('aria-expanded', open ? 'true' : 'false');
            header.classList.toggle('lr-open', open);
            children.forEach(function (child) {
                child.hidden = !open;
            });
        });
        return [header].concat(children);
    }

    // Quallenkino-Funktionen, die nicht ständig gebraucht werden
    function quallenkinoMenuItems() {
        var items = [];
        if (avatarsEnabled() && userId()) {
            items.push(accountMenuItem('face', 'Profilbild wählen', function () {
                openAvatarEditor({});
            }));
        }
        if (state.config && state.config.TraktPersonal && userId()) {
            items.push(accountMenuItem('recommend', 'Trakt-Empfehlungen', openTraktDialog));
        }
        if (userId()) {
            items.push(accountMenuItem('psychology', 'Was Quallenkino sich merkt', openBehaviorDialog));
        }
        if (state.liveTv && state.config && state.config.Reminders) {
            items.push(accountMenuItem('event', 'Erinnerungen im Kalender', openCalendarDialog));
        }
        if (state.config && state.config.PlayerEnabled && !appPlaysNatively() && storageGet('lrWeb.classicPlayer') === '1') {
            items.push(accountMenuItem('movie', 'Quallenkino-Player einschalten', function () {
                storageSet('lrWeb.classicPlayer', null);
            }));
        }
        if (diagOn()) {
            items.push(accountMenuItem('bug_report', 'Diagnose senden', function () {
                diagSend('Diagnose gesendet von Hand (' + diagEnvironment() + '), letzte Schritte:');
                diag.steps.slice(-30).forEach(function (step) {
                    diagSend('  ' + step);
                });
            }));
        }
        return items;
    }

    // Profilmenü aufgeräumt: direkt „Profil wechseln“, „Zufälliger Titel“ und weggeräumte Knöpfe anderer Plugins,
    // dann „Quallenkino“ und „Mehr …“ zum Aufklappen, darunter die Einträge von Jellyfin
    function extendUserMenu(list, tidy) {
        if (!list) {
            return;
        }
        // Das Menü bleibt im DOM – bei jedem Öffnen neu aufbauen, damit später hinzugekommene Knöpfe dabei sind
        list.querySelectorAll('.lr-menu-item, .lr-menu-divider').forEach(function (node) {
            node.remove();
        });
        list.querySelectorAll('.lr-menu-hidden').forEach(function (node) {
            node.classList.remove('lr-menu-hidden');
        });
        var direct = [];
        var more = [];
        if (profilesEnabled()) {
            direct.push(accountMenuItem('switch_account', 'Profil wechseln', function () {
                rememberProfile();
                showProfilePicker({});
            }));
        }
        if (tidy) {
            menuSources().forEach(function (entry) {
                var label = entry.node.id === 'randomItemButton' ? 'Zufälliger Titel' : entry.label;
                var item = proxyMenuItem(entry.node, label);
                (RARE_MENU_LABELS.test(label) ? more : direct).push(item);
            });
            // Seltene Einträge von Jellyfin selbst (z. B. Quick Connect) verstecken und unter „Mehr …“ anbieten
            list.querySelectorAll(':scope > .MuiMenuItem-root:not(.lr-menu-item)').forEach(function (native) {
                var label = (native.textContent || '').trim();
                if (label && RARE_MENU_LABELS.test(label)) {
                    native.classList.add('lr-menu-hidden');
                    var item = accountMenuItem(null, label, null);
                    var symbol = native.querySelector('svg');
                    if (symbol) {
                        item.querySelector('.lr-menu-icon').appendChild(symbol.cloneNode(true));
                    }
                    onActivate(item, function () {
                        native.click();
                    });
                    more.push(item);
                }
            });
        }
        var items = direct
            .concat(menuGroup('quallenkino', 'auto_awesome', 'Quallenkino', quallenkinoMenuItems()))
            .concat(menuGroup('more', 'more_horiz', 'Mehr …', more));
        if (!items.length) {
            return;
        }
        var first = list.firstChild;
        items.forEach(function (item) {
            list.insertBefore(item, first);
        });
        var divider = el('li', 'lr-menu-divider');
        divider.setAttribute('role', 'separator');
        list.insertBefore(divider, first);
    }

    // Beim Öffnen des Profilmenüs (Klick auf das Profilbild) die Einträge ergänzen
    document.addEventListener('click', function (e) {
        var target = e.target && e.target.closest ? e.target.closest('header.MuiAppBar-root button') : null;
        if (!target || !target.querySelector('.MuiAvatar-root')) {
            return;
        }
        var tidy = document.body.classList.contains('lr-tidy-header');
        if (!tidy && !profilesEnabled() && !avatarsEnabled()) {
            return;
        }
        var tries = 0;
        var timer = window.setInterval(function () {
            var list = document.querySelector('#app-user-menu ul');
            tries++;
            if (list || tries > 20) {
                window.clearInterval(timer);
                extendUserMenu(list, tidy);
                if (tidy) {
                    markLogout(list);
                }
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
        if (view && view.nodeType === 1) {
            diagStep('Ansicht ' + (view.id || String(view.className).split(' ')[0]) + ' ' + window.location.hash);
        }
        if (view && view.id === 'videoOsdPage') {
            playState.osdAt = Date.now();
            startSkipWatcher();
            startOwnPlayer();
            diagWatchClassicPlayer();
        } else {
            stopSkipWatcher();
            stopOwnPlayer();
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

    // ---------------------------------------------------------------- Player im Quallenkino-Stil (Test, nur für freigeschaltete Benutzer)
    // Eigene Steuerleiste über der Wiedergabe. Abgespielt wird weiter von Jellyfin: Spulen über Jellyfins Zeitleiste
    // (stimmt auch beim Transkodieren und bei Live-TV), Knöpfe lösen Jellyfins eigene Knöpfe aus, Ton und Untertitel
    // werden über die eigene Sitzung umgeschaltet (wie bei der Fernbedienung in Jellyfin).

    var PLAYER_HIDE_MS = 3000;
    var SEEK_STEP_TICKS = 10 * 10000000;
    var SPEEDS = [0.5, 0.75, 1, 1.25, 1.5, 2];
    var nf = { root: null, tick: null, hideTimer: null, session: null, sessionAt: 0, itemId: null, item: null, trickplay: null, mediaSourceId: null, dragging: false, lastTap: 0, panel: null };

    // Spielt die iOS-App selbst ab (Einstellung „nativer Videoplayer“), gibt es kein Video im WebView zu steuern
    function appPlaysNatively() {
        return !!(window.ExpoAppSettings && window.ExpoAppSettings.isNativeVideoPlayerEnabled);
    }

    function playerWanted() {
        return !!(state.config && state.config.PlayerEnabled) && storageGet('lrWeb.classicPlayer') !== '1' && !appPlaysNatively();
    }

    function osdNode(selector) {
        return document.querySelector('#videoOsdPage ' + selector);
    }

    function nativeVisible(selector) {
        var node = osdNode(selector);
        return node && !node.classList.contains('hide') ? node : null;
    }

    function clock(seconds) {
        seconds = Math.max(0, Math.floor(seconds || 0));
        var h = Math.floor(seconds / 3600);
        var m = Math.floor(seconds % 3600 / 60);
        var sec = seconds % 60;
        return (h ? h + ':' + ('0' + m).slice(-2) : m) + ':' + ('0' + sec).slice(-2);
    }

    // Jellyfins aktueller lokaler Player (über den playbackManager) – Grundlage der Steuerung, auch ohne fernsteuerbare Sitzung
    function nfManager() {
        var manager = playbackManager();
        var player = null;
        try {
            player = manager ? manager.getCurrentPlayer() : null;
        } catch (e) {
            player = null;
        }
        return manager && player ? { manager: manager, player: player } : null;
    }

    function nfRuntime() {
        if (nf.item && nf.item.RunTimeTicks) {
            return nf.item.RunTimeTicks;
        }
        var pm = nfManager();
        try {
            return pm ? pm.manager.duration(pm.player) || 0 : 0;
        } catch (e) {
            return 0;
        }
    }

    function nfLive() {
        return !!nf.item && (nf.item.Type === 'TvChannel' || nf.item.Type === 'Program' || nf.item.MediaType === 'Video' && !nf.item.RunTimeTicks);
    }

    function nfPositionTicks() {
        var pm = nfManager();
        if (pm) {
            try {
                return pm.manager.getCurrentTicks(pm.player) || 0;
            } catch (e) {
                // weiter mit Jellyfins Regler
            }
        }
        var slider = osdNode('.osdPositionSlider');
        return (slider ? parseFloat(slider.value) || 0 : 0) / 100 * nfRuntime();
    }

    function nfPercent() {
        var runtime = nfRuntime();
        return runtime ? Math.max(0, Math.min(100, nfPositionTicks() / runtime * 100)) : 0;
    }

    function nfSeekTicks(ticks) {
        var runtime = nfRuntime();
        if (!runtime || nfLive()) {
            diagStep('Spulen nicht möglich (Laufzeit ' + runtime + ', live ' + nfLive() + ')', false, true);
            return;
        }
        ticks = Math.max(0, Math.min(runtime - 10000000, ticks));
        var pm = nfManager();
        if (pm) {
            pm.manager.seek(Math.round(ticks), pm.player);
            return;
        }
        var slider = osdNode('.osdPositionSlider');
        if (slider) {
            slider.value = ticks / runtime * 100;
            slider.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }

    function nfTogglePause() {
        var pm = nfManager();
        if (pm) {
            pm.manager.playPause(pm.player);
            return;
        }
        var pause = osdNode('.btnPause');
        if (pause) {
            pause.click();
        }
    }

    function nfIcon(node, name) {
        var iconNode = node.querySelector('.lr-icon');
        if (iconNode && iconNode.textContent !== name) {
            iconNode.textContent = name;
        }
    }

    function nfButton(cls, iconName, title, onClick) {
        var b = button('lr-nf-btn ' + cls, iconName, null, function (e) {
            diagStep('Player-Taste: ' + title + ' (Jellyfin-Player ' + (nfManager() ? 'gefunden' : 'nicht gefunden') + ')', false, true);
            try {
                onClick(e);
            } catch (err) {
                diagStep('Fehler bei „' + title + '“: ' + errorText(err), false, true);
            }
        });
        b.title = title;
        b.setAttribute('aria-label', title);
        return b;
    }

    function nfRefreshSession(force) {
        if (!force && Date.now() - nf.sessionAt < 3000) {
            return;
        }
        nf.sessionAt = Date.now();
        ownSession().then(function (session) {
            nf.session = session;
            var item = session && session.NowPlayingItem;
            var sourceId = session && session.PlayState && session.PlayState.MediaSourceId;
            var pm = nfManager();
            if (!item && pm) {
                // Sitzung nicht fernsteuerbar (iOS-App) – Titel vom lokalen Player
                try {
                    item = pm.manager.currentItem(pm.player);
                    sourceId = (pm.manager.currentMediaSource(pm.player) || {}).Id;
                } catch (e) {
                    item = null;
                }
            }
            if (!item || item.Id === nf.itemId) {
                return;
            }
            nf.itemId = item.Id;
            nf.item = item;
            nf.mediaSourceId = sourceId;
            nf.trickplay = null;
            nfTitle();
            getJson(itemsQuery('Items/' + item.Id).replace('&Fields=' + FIELDS, '&Fields=' + FIELDS + ',Trickplay,MediaSources,MediaStreams,Chapters'))
                .then(function (full) {
                    if (nf.itemId !== full.Id) {
                        return;
                    }
                    nf.item = full;
                    var sourceId = nf.mediaSourceId || (full.MediaSources && full.MediaSources[0] && full.MediaSources[0].Id);
                    var resolutions = full.Trickplay && sourceId ? full.Trickplay[sourceId] : null;
                    if (resolutions) {
                        var best = null;
                        Object.keys(resolutions).forEach(function (key) {
                            var info = resolutions[key];
                            if (!best || (info.Width <= 320 && info.Width > best.Width) || (best.Width > 320 && info.Width < best.Width)) {
                                best = info;
                            }
                        });
                        nf.trickplay = best ? { info: best, sourceId: sourceId } : null;
                    }
                    nfTitle();
                }).catch(function () {
                    // ohne Zusatzinfos
                });
        });
    }

    function nfTitle() {
        if (!nf.root || !nf.item) {
            return;
        }
        var item = nf.item;
        var main = item.Type === 'Episode' ? item.SeriesName : (item.Type === 'TvChannel' ? item.Name : item.Name);
        var sub = '';
        if (item.Type === 'Episode') {
            sub = subtitle(item);
        } else if (item.Type === 'TvChannel' && item.CurrentProgram) {
            sub = item.CurrentProgram.Name || '';
        }
        nf.root.querySelector('.lr-nf-title').textContent = main || '';
        nf.root.querySelector('.lr-nf-subtitle').textContent = sub;
        nf.root.classList.toggle('lr-nf-live', nfLive());
        nf.root.classList.toggle('lr-nf-episode', item.Type === 'Episode');
    }

    function nfShow() {
        if (!nf.root) {
            return;
        }
        nf.root.classList.add('lr-nf-visible');
        document.documentElement.classList.remove('lr-nf-idle');
        window.clearTimeout(nf.hideTimer);
        nf.hideTimer = window.setTimeout(nfMaybeHide, PLAYER_HIDE_MS);
    }

    function nfMaybeHide() {
        var video = currentVideo();
        if (!nf.root || nf.panel || nf.dragging || (video && video.paused) || nf.root.matches(':hover') && !isTouch && nf.root.querySelector('.lr-nf-bottom:hover, .lr-nf-top:hover')) {
            nf.hideTimer = window.setTimeout(nfMaybeHide, PLAYER_HIDE_MS);
            return;
        }
        nf.root.classList.remove('lr-nf-visible');
        document.documentElement.classList.add('lr-nf-idle');
    }

    function nfClosePanel() {
        if (nf.panel) {
            nf.panel.remove();
            nf.panel = null;
        }
    }

    function nfPanel(cls, anchor) {
        nfClosePanel();
        var panel = el('div', 'lr-nf-panel ' + cls);
        panel.addEventListener('click', function (e) {
            e.stopPropagation();
        });
        if (cls.indexOf('lr-nf-side') >= 0) {
            var closeSide = button('lr-round lr-close', 'close', null, nfClosePanel);
            closeSide.title = 'Schließen';
            panel.appendChild(closeSide);
        }
        nf.root.appendChild(panel);
        nf.panel = panel;
        if (anchor && !panel.classList.contains('lr-nf-side')) {
            var rect = anchor.getBoundingClientRect();
            panel.style.right = Math.max(8, window.innerWidth - rect.right - 8) + 'px';
        }
        nfShow();
        return panel;
    }

    function nfSendCommand(name, index) {
        var pm = nfManager();
        if (pm) {
            if (name === 'SetAudioStreamIndex') {
                pm.manager.setAudioStreamIndex(index, pm.player);
            } else {
                pm.manager.setSubtitleStreamIndex(index, pm.player);
            }
            return Promise.resolve();
        }
        var session = nf.session;
        if (!session) {
            return Promise.reject(new Error('keine Sitzung'));
        }
        return api().ajax({
            type: 'POST',
            url: url('Sessions/' + session.Id + '/Command'),
            data: JSON.stringify({ Name: name, Arguments: { Index: String(index) } }),
            contentType: 'application/json'
        }).then(function () {
            window.setTimeout(function () {
                nfRefreshSession(true);
            }, 800);
        });
    }

    function nfStreams() {
        var item = nf.item || {};
        var sources = item.MediaSources || [];
        var source = sources.filter(function (s) {
            return s.Id === nf.mediaSourceId;
        })[0] || sources[0];
        return (source && source.MediaStreams) || item.MediaStreams || [];
    }

    function nfStreamName(stream) {
        return stream.DisplayTitle || [stream.Language, stream.Codec].filter(Boolean).join(' · ') || ('Spur ' + stream.Index);
    }

    function nfAudioSubtitleMenu(anchor) {
        var streams = nfStreams();
        var audio = streams.filter(function (s) {
            return s.Type === 'Audio';
        });
        var subs = streams.filter(function (s) {
            return s.Type === 'Subtitle';
        });
        var pmTracks = nfManager();
        if (!nf.session && !pmTracks || (!audio.length && !subs.length)) {
            // Ohne Sitzung: Jellyfins eigene Menüs
            var nativeButton = nativeVisible('.btnSubtitles') || nativeVisible('.btnAudio');
            if (nativeButton) {
                clickNative(nativeButton, anchor);
            }
            return;
        }
        var playState = nf.session && nf.session.PlayState || {};
        if (pmTracks) {
            try {
                playState = {
                    AudioStreamIndex: pmTracks.manager.getAudioStreamIndex(pmTracks.player),
                    SubtitleStreamIndex: pmTracks.manager.getSubtitleStreamIndex(pmTracks.player)
                };
            } catch (e) {
                // Stand aus der Sitzung
            }
        }
        var panel = nfPanel('lr-nf-tracks', anchor);
        var column = function (title, list, current, command, offLabel) {
            var col = el('div', 'lr-nf-col');
            col.appendChild(el('h3', 'lr-nf-col-title', title));
            var entries = list.map(function (s) {
                return { index: s.Index, name: nfStreamName(s) };
            });
            if (offLabel) {
                entries.unshift({ index: -1, name: offLabel });
            }
            entries.forEach(function (entry) {
                var selected = entry.index === current || (entry.index === -1 && (current === null || current === undefined || current < 0));
                var row = button('lr-nf-option' + (selected ? ' lr-on' : ''), selected ? 'check' : null, entry.name, function () {
                    nfSendCommand(command, entry.index).catch(function () {
                        var fallback = nativeVisible(command === 'SetAudioStreamIndex' ? '.btnAudio' : '.btnSubtitles');
                        if (fallback) {
                            clickNative(fallback, anchor);
                        }
                    });
                    nfClosePanel();
                });
                col.appendChild(row);
            });
            panel.appendChild(col);
        };
        if (audio.length) {
            column('Ton', audio, playState.AudioStreamIndex, 'SetAudioStreamIndex');
        }
        column('Untertitel', subs, playState.SubtitleStreamIndex, 'SetSubtitleStreamIndex', 'Aus');
    }

    function nfSpeedMenu(anchor) {
        var video = currentVideo();
        var panel = nfPanel('lr-nf-speed', anchor);
        panel.appendChild(el('h3', 'lr-nf-col-title', 'Geschwindigkeit'));
        var rowNode = el('div', 'lr-nf-speeds');
        SPEEDS.forEach(function (speed) {
            var on = video && Math.abs(video.playbackRate - speed) < 0.01;
            rowNode.appendChild(button('lr-nf-chip' + (on ? ' lr-on' : ''), null, speed === 1 ? '1× (Normal)' : speed.toString().replace('.', ',') + '×', function () {
                var v = currentVideo();
                if (v) {
                    v.playbackRate = speed;
                }
                nfClosePanel();
            }));
        });
        panel.appendChild(rowNode);
    }

    function nfEpisodesMenu() {
        var item = nf.item;
        if (!item || item.Type !== 'Episode' || !item.SeriesId) {
            return;
        }
        var panel = nfPanel('lr-nf-side lr-nf-episodes');
        panel.appendChild(el('h3', 'lr-nf-col-title', item.SeriesName || 'Folgen'));
        var list = el('div', 'lr-episodes');
        list.appendChild(el('div', 'lr-spinner'));
        panel.appendChild(list);
        getJson(itemsQuery('Shows/' + item.SeriesId + '/Episodes?seasonId=' + (item.SeasonId || ''))).then(function (result) {
            list.innerHTML = '';
            itemsOf(result).forEach(function (episode) {
                var row = episodeRow(episode);
                if (episode.Id === item.Id) {
                    row.classList.add('lr-current');
                    window.setTimeout(function () {
                        row.scrollIntoView({ block: 'center' });
                    }, 50);
                }
                list.appendChild(row);
            });
        });
    }

    function nfMoreMenu(anchor) {
        var panel = nfPanel('lr-nf-more', anchor);
        var add = function (iconName, label, onClick) {
            panel.appendChild(button('lr-nf-option', iconName, label, function () {
                nfClosePanel();
                onClick();
            }));
        };
        var settings = osdNode('.btnVideoOsdSettings');
        if (settings) {
            add('settings', 'Qualität & Wiedergabe', function () {
                clickNative(settings, anchor);
            });
        }
        if (nativeVisible('.btnPreviousChapter')) {
            add('skip_previous', 'Vorheriges Kapitel', function () {
                osdNode('.btnPreviousChapter').click();
            });
        }
        if (nativeVisible('.btnNextChapter')) {
            add('skip_next', 'Nächstes Kapitel', function () {
                osdNode('.btnNextChapter').click();
            });
        }
        if (nativeVisible('.btnPip')) {
            add('picture_in_picture_alt', 'Bild in Bild', function () {
                osdNode('.btnPip').click();
            });
        }
        if (nativeVisible('.btnAirPlay')) {
            add('airplay', 'AirPlay', function () {
                osdNode('.btnAirPlay').click();
            });
        }
        if (nativeVisible('.btnRecord')) {
            add('fiber_manual_record', 'Aufnehmen', function () {
                osdNode('.btnRecord').click();
            });
        }
        add('tv_off', 'Klassischer Player', function () {
            storageSet('lrWeb.classicPlayer', '1');
            stopOwnPlayer();
        });
    }

    function nfChannel(step) {
        var item = nf.item;
        if (!item || item.Type !== 'TvChannel') {
            return;
        }
        getJson('LiveTv/Channels?userId=' + userId() + '&EnableImages=false&EnableUserData=false&SortBy=SortName').then(function (result) {
            var channels = itemsOf(result);
            var index = channels.map(function (c) {
                return c.Id;
            }).indexOf(item.Id);
            var next = channels[(index + step + channels.length) % channels.length];
            if (next) {
                playNow({ Id: next.Id, Type: 'TvChannel' }, 0);
            }
        });
    }

    function nfPreview(pct) {
        var bubble = nf.root.querySelector('.lr-nf-preview');
        var runtime = nfRuntime();
        if (!runtime) {
            bubble.classList.add('lr-hidden');
            return;
        }
        var ticks = pct / 100 * runtime;
        bubble.querySelector('.lr-nf-preview-time').textContent = clock(ticks / 10000000);
        var thumb = bubble.querySelector('.lr-nf-preview-thumb');
        var tp = nf.trickplay;
        if (tp) {
            var info = tp.info;
            var tile = Math.floor(ticks / 10000 / info.Interval);
            var perImage = info.TileWidth * info.TileHeight;
            var imageIndex = Math.floor(tile / perImage);
            var offset = tile % perImage;
            thumb.style.width = info.Width + 'px';
            thumb.style.height = info.Height + 'px';
            thumb.style.backgroundImage = 'url("' + url('Videos/' + nf.item.Id + '/Trickplay/' + info.Width + '/' + imageIndex + '.jpg?MediaSourceId=' + tp.sourceId + '&ApiKey=' + api().accessToken()) + '")';
            thumb.style.backgroundPosition = -(offset % info.TileWidth) * info.Width + 'px ' + -Math.floor(offset / info.TileWidth) * info.Height + 'px';
            thumb.classList.remove('lr-hidden');
        } else {
            thumb.classList.add('lr-hidden');
        }
        var bar = nf.root.querySelector('.lr-nf-bar');
        var rect = bar.getBoundingClientRect();
        var half = (tp ? tp.info.Width : 60) / 2;
        bubble.style.left = Math.max(half, Math.min(rect.width - half, pct / 100 * rect.width)) + 'px';
        bubble.classList.remove('lr-hidden');
    }

    function buildOwnPlayer() {
        var root = el('div', 'lr-nf');
        // Fläche zum Antippen/Klicken (Steuerung zeigen, am PC Pause, Doppeltipp ±10 s)
        var surface = el('div', 'lr-nf-surface');
        surface.addEventListener('click', function (e) {
            diagStep('Tippen: Player-Fläche');
            if (nf.panel) {
                nfClosePanel();
                return;
            }
            var now = Date.now();
            var third = window.innerWidth / 3;
            if (isTouch) {
                if (now - nf.lastTap < 300 && !nfLive()) {
                    if (e.clientX < third) {
                        nfSeekTicks(nfPositionTicks() - SEEK_STEP_TICKS);
                    } else if (e.clientX > third * 2) {
                        nfSeekTicks(nfPositionTicks() + SEEK_STEP_TICKS);
                    }
                    nf.lastTap = 0;
                    nfShow();
                    return;
                }
                nf.lastTap = now;
                if (root.classList.contains('lr-nf-visible')) {
                    root.classList.remove('lr-nf-visible');
                } else {
                    nfShow();
                }
                return;
            }
            nfTogglePause();
            nfShow();
        });
        surface.addEventListener('dblclick', function () {
            if (!isTouch) {
                var fs = nativeVisible('.btnFullscreen');
                if (fs) {
                    fs.click();
                }
            }
        });
        root.appendChild(surface);

        var top = el('div', 'lr-nf-top');
        top.appendChild(nfButton('lr-nf-back', 'arrow_back', 'Zurück', function () {
            window.history.back();
        }));
        var titleBox = el('div', 'lr-nf-titlebox');
        titleBox.appendChild(el('div', 'lr-nf-title'));
        titleBox.appendChild(el('div', 'lr-nf-subtitle'));
        top.appendChild(titleBox);
        root.appendChild(top);

        var center = el('div', 'lr-nf-center');
        center.appendChild(nfButton('lr-nf-seek lr-nf-back10', 'replay_10', '10 Sekunden zurück', function () {
            nfSeekTicks(nfPositionTicks() - SEEK_STEP_TICKS);
            nfShow();
        }));
        center.appendChild(nfButton('lr-nf-play lr-nf-bigplay', 'pause', 'Abspielen/Pause', function () {
            nfTogglePause();
            nfShow();
        }));
        center.appendChild(nfButton('lr-nf-seek lr-nf-fwd10', 'forward_10', '10 Sekunden vor', function () {
            nfSeekTicks(nfPositionTicks() + SEEK_STEP_TICKS);
            nfShow();
        }));
        center.appendChild(nfButton('lr-nf-channel lr-nf-chup', 'keyboard_arrow_up', 'Nächster Sender', function () {
            nfChannel(1);
        }));
        center.appendChild(nfButton('lr-nf-channel lr-nf-chdown', 'keyboard_arrow_down', 'Vorheriger Sender', function () {
            nfChannel(-1);
        }));
        root.appendChild(center);

        var bottom = el('div', 'lr-nf-bottom');
        var barRow = el('div', 'lr-nf-barrow');
        var bar = el('div', 'lr-nf-bar');
        bar.appendChild(el('div', 'lr-nf-buffer'));
        bar.appendChild(el('div', 'lr-nf-fill'));
        bar.appendChild(el('div', 'lr-nf-knob'));
        var preview = el('div', 'lr-nf-preview lr-hidden');
        preview.appendChild(el('div', 'lr-nf-preview-thumb lr-hidden'));
        preview.appendChild(el('div', 'lr-nf-preview-time'));
        bar.appendChild(preview);
        barRow.appendChild(bar);
        barRow.appendChild(el('div', 'lr-nf-remaining'));
        barRow.appendChild(el('div', 'lr-nf-livebadge', 'LIVE'));
        bottom.appendChild(barRow);

        var pctFromEvent = function (e) {
            var rect = bar.getBoundingClientRect();
            var x = (e.touches && e.touches[0] ? e.touches[0].clientX : e.clientX) - rect.left;
            return Math.max(0, Math.min(100, x / rect.width * 100));
        };
        var dragPct = 0;
        var startDrag = function (e) {
            if (nfLive() || !nfRuntime()) {
                return;
            }
            nf.dragging = true;
            dragPct = pctFromEvent(e);
            root.classList.add('lr-nf-dragging');
            nfPreview(dragPct);
            e.preventDefault();
        };
        var moveDrag = function (e) {
            if (!nf.dragging) {
                if (!isTouch && e.target && bar.contains(e.target)) {
                    nfPreview(pctFromEvent(e));
                }
                return;
            }
            dragPct = pctFromEvent(e);
            root.querySelector('.lr-nf-fill').style.width = dragPct + '%';
            root.querySelector('.lr-nf-knob').style.left = dragPct + '%';
            nfPreview(dragPct);
        };
        var endDrag = function () {
            if (!nf.dragging) {
                return;
            }
            nf.dragging = false;
            root.classList.remove('lr-nf-dragging');
            preview.classList.add('lr-hidden');
            nfSeekTicks(dragPct / 100 * nfRuntime());
            nfShow();
        };
        bar.addEventListener('mousedown', startDrag);
        bar.addEventListener('touchstart', startDrag, { passive: false });
        document.addEventListener('mousemove', moveDrag);
        document.addEventListener('touchmove', moveDrag, { passive: true });
        document.addEventListener('mouseup', endDrag);
        document.addEventListener('touchend', endDrag);
        bar.addEventListener('mouseleave', function () {
            if (!nf.dragging) {
                preview.classList.add('lr-hidden');
            }
        });
        nf.cleanupDrag = function () {
            document.removeEventListener('mousemove', moveDrag);
            document.removeEventListener('touchmove', moveDrag);
            document.removeEventListener('mouseup', endDrag);
            document.removeEventListener('touchend', endDrag);
        };

        var row = el('div', 'lr-nf-row');
        var left = el('div', 'lr-nf-left');
        left.appendChild(nfButton('lr-nf-play', 'pause', 'Abspielen/Pause', function () {
            nfTogglePause();
            nfShow();
        }));
        left.appendChild(nfButton('lr-nf-seek', 'replay_10', '10 Sekunden zurück', function () {
            nfSeekTicks(nfPositionTicks() - SEEK_STEP_TICKS);
        }));
        left.appendChild(nfButton('lr-nf-seek', 'forward_10', '10 Sekunden vor', function () {
            nfSeekTicks(nfPositionTicks() + SEEK_STEP_TICKS);
        }));
        if (!isTouch) {
            var volume = el('div', 'lr-nf-volume');
            volume.appendChild(nfButton('lr-nf-mute', 'volume_up', 'Ton aus/an', function () {
                var mute = osdNode('.buttonMute');
                if (mute) {
                    mute.click();
                }
            }));
            var range = el('input', 'lr-nf-volrange');
            range.type = 'range';
            range.min = '0';
            range.max = '100';
            range.addEventListener('input', function () {
                var slider = osdNode('.osdVolumeSlider');
                if (slider) {
                    slider.value = range.value;
                    slider.dispatchEvent(new Event('input', { bubbles: true }));
                }
            });
            volume.appendChild(range);
            left.appendChild(volume);
        }
        row.appendChild(left);
        row.appendChild(el('div', 'lr-nf-rowtitle'));
        var right = el('div', 'lr-nf-right');
        right.appendChild(nfButton('lr-nf-next', 'skip_next', 'Nächste Folge', function () {
            var next = nativeVisible('.btnNextTrack');
            if (next) {
                next.click();
            }
        }));
        var episodes = nfButton('lr-nf-episodes-btn', 'video_library', 'Folgen', function () {
            nfEpisodesMenu();
        });
        right.appendChild(episodes);
        var tracks = nfButton('lr-nf-tracks-btn', null, 'Ton & Untertitel', function () {
            nfAudioSubtitleMenu(tracks);
        });
        // Sprechblase mit Zeilen statt „CC“
        var bubble = el('span', 'lr-icon lr-svg-icon');
        bubble.setAttribute('aria-hidden', 'true');
        bubble.innerHTML = '<svg viewBox="0 0 24 24" width="1em" height="1em"><path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linejoin="round" '
            + 'd="M4 4.5h16a1.5 1.5 0 0 1 1.5 1.5v10a1.5 1.5 0 0 1-1.5 1.5H10.5l-4.5 3.5v-3.5H4A1.5 1.5 0 0 1 2.5 16V6A1.5 1.5 0 0 1 4 4.5z"/>'
            + '<path fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" d="M6.5 9.5h6.5M15.5 9.5h2M6.5 13h3M12 13h5.5"/></svg>';
        tracks.insertBefore(bubble, tracks.firstChild);
        right.appendChild(tracks);
        var speed = nfButton('lr-nf-speed-btn', 'speed', 'Geschwindigkeit', function () {
            nfSpeedMenu(speed);
        });
        right.appendChild(speed);
        right.appendChild(nfButton('lr-nf-fullscreen', 'fullscreen', 'Vollbild', function () {
            var fs = nativeVisible('.btnFullscreen');
            if (fs) {
                fs.click();
            }
        }));
        var more = nfButton('lr-nf-more-btn', 'more_vert', 'Mehr', function () {
            nfMoreMenu(more);
        });
        right.appendChild(more);
        row.appendChild(right);
        bottom.appendChild(row);
        root.appendChild(bottom);

        root.addEventListener('mousemove', nfShow, { passive: true });
        return root;
    }

    function nfUpdate() {
        var root = nf.root;
        if (!root) {
            return;
        }
        if (!document.querySelector('#videoOsdPage:not(.hide)')) {
            stopOwnPlayer();
            return;
        }
        nfRefreshSession(false);
        var video = currentVideo();
        nfPrepareVideo(video);
        nfWatchVideo(video);
        var paused = video ? video.paused : false;
        // Beim Laden nur der Ladekreis, kein Pause-Symbol darin
        var loading = !video || video.readyState < 2 || !paused && (video.readyState < 3 || video.seeking);
        root.classList.toggle('lr-nf-loading', loading);
        root.querySelectorAll('.lr-nf-play').forEach(function (b) {
            nfIcon(b, paused ? 'play_arrow' : 'pause');
        });
        root.classList.toggle('lr-nf-paused', paused);
        if (paused) {
            nfShow();
        }
        var live = nfLive();
        var pct = nfPercent();
        if (!nf.dragging) {
            root.querySelector('.lr-nf-fill').style.width = pct + '%';
            root.querySelector('.lr-nf-knob').style.left = pct + '%';
        }
        if (video && video.buffered && video.buffered.length && video.duration && isFinite(video.duration)) {
            var bufferedEnd = video.buffered.end(video.buffered.length - 1) / video.duration * 100;
            root.querySelector('.lr-nf-buffer').style.width = Math.min(100, bufferedEnd) + '%';
        }
        var runtime = nfRuntime();
        root.querySelector('.lr-nf-remaining').textContent = runtime && !live ? '-' + clock((runtime - nfPositionTicks()) / 10000000) : '';
        var mute = root.querySelector('.lr-nf-mute');
        if (mute && video) {
            nfIcon(mute, video.muted || video.volume === 0 ? 'volume_off' : 'volume_up');
            var range = root.querySelector('.lr-nf-volrange');
            if (document.activeElement !== range) {
                range.value = Math.round((video.muted ? 0 : video.volume) * 100);
            }
        }
        root.querySelector('.lr-nf-next').classList.toggle('lr-gone', !nativeVisible('.btnNextTrack'));
        // iPhone kennt kein Vollbild für Seitenelemente – Jellyfin schöbe das Video in den nativen Vollbild-Player (ohne diese Leiste)
        var canFullscreen = !!(document.fullscreenEnabled || document.webkitFullscreenEnabled);
        root.querySelector('.lr-nf-fullscreen').classList.toggle('lr-gone', !canFullscreen || !nativeVisible('.btnFullscreen'));
        root.querySelector('.lr-nf-episodes-btn').classList.toggle('lr-gone', !(nf.item && nf.item.Type === 'Episode'));
    }

    // Video im Seitenfluss halten: keine nativen Bedienelemente, inline abspielen; in der iOS-App den nativen
    // Vollbild-Player sofort wieder verlassen (dort wäre die Quallenkino-Leiste weg)
    function nfPrepareVideo(video) {
        if (!video || nf.preparedVideo === video) {
            return;
        }
        nf.preparedVideo = video;
        video.controls = false;
        video.removeAttribute('controls');
        video.playsInline = true;
        video.setAttribute('playsinline', '');
        video.setAttribute('webkit-playsinline', '');
        video.addEventListener('webkitbeginfullscreen', function () {
            if (!nf.root || !isNativeIos || typeof video.webkitExitFullscreen !== 'function') {
                return;
            }
            diagStep('Nativer Vollbild-Player geöffnet – wird verlassen', false, true);
            window.setTimeout(function () {
                try {
                    video.webkitExitFullscreen();
                } catch (e) {
                    diagStep('Vollbild verlassen fehlgeschlagen: ' + errorText(e), false, true);
                }
            }, 50);
        });
    }

    // Diagnose: Video-Ereignisse und alle 5 s ein Lebenszeichen
    function nfWatchVideo(video) {
        if (!diagOn()) {
            return;
        }
        if (video && nf.diagVideo !== video) {
            nf.diagVideo = video;
            diagStep('Video-Element gefunden (' + (video.className || 'ohne Klasse') + ')');
            ['loadstart', 'loadedmetadata', 'playing', 'waiting', 'stalled', 'pause', 'ended', 'emptied', 'webkitbeginfullscreen', 'webkitendfullscreen'].forEach(function (name) {
                video.addEventListener(name, function () {
                    diagStep('Video ' + name + ' t=' + Math.round(video.currentTime) + ' rs=' + video.readyState
                        + (name === 'loadedmetadata' ? ' ' + video.videoWidth + 'x' + video.videoHeight : ''));
                });
            });
            video.addEventListener('error', function () {
                var code = video.error ? video.error.code + ' ' + (video.error.message || '') : '?';
                diagStep('Video-Fehler ' + code);
                diagSend('Video-Fehler ' + code);
            });
        }
        if (!nf.diagBeat || Date.now() - nf.diagBeat > 5000) {
            nf.diagBeat = Date.now();
            diagStep('Lebenszeichen ' + (video ? 't=' + Math.round(video.currentTime) + ' rs=' + video.readyState + (video.paused ? ' Pause' : '') : 'ohne Video'), true);
        }
    }

    // Diagnose auch mit Jellyfins eigenem Player (z. B. in der iOS-App, wo der Quallenkino-Player aus ist)
    function diagWatchClassicPlayer() {
        if (!diagOn() || nf.root || nf.diagTimer) {
            return;
        }
        nf.diagTimer = window.setInterval(function () {
            if (!document.querySelector('#videoOsdPage:not(.hide)')) {
                window.clearInterval(nf.diagTimer);
                nf.diagTimer = null;
                return;
            }
            if (!nf.root) {
                nfWatchVideo(currentVideo());
            }
        }, 1000);
    }

    function startOwnPlayer() {
        if (nf.root || !playerWanted()) {
            return;
        }
        nf.itemId = null;
        nf.item = null;
        nf.sessionAt = 0;
        diagStep('Quallenkino-Player startet');
        nf.root = buildOwnPlayer();
        document.body.appendChild(nf.root);
        document.documentElement.classList.add('lr-player');
        nfRefreshSession(true);
        nfShow();
        nf.tick = window.setInterval(nfUpdate, 250);
    }

    function stopOwnPlayer() {
        if (nf.root) {
            diagStep('Quallenkino-Player beendet');
        }
        window.clearInterval(nf.tick);
        window.clearTimeout(nf.hideTimer);
        nf.tick = null;
        if (nf.cleanupDrag) {
            nf.cleanupDrag();
            nf.cleanupDrag = null;
        }
        nfClosePanel();
        if (nf.root) {
            nf.root.remove();
            nf.root = null;
        }
        document.documentElement.classList.remove('lr-player', 'lr-nf-idle');
    }

    document.addEventListener('keydown', function (e) {
        if (!nf.root) {
            return;
        }
        if (e.key === 'Escape' && nf.panel) {
            nfClosePanel();
            e.stopPropagation();
            e.preventDefault();
            return;
        }
        nfShow();
    }, true);

    // ---------------------------------------------------------------- „Wer schaut?“ – Profilauswahl ohne PIN

    // Die Zugänge der Profile bleiben nur in diesem Browser (wie Jellyfin selbst seine Anmeldung speichert).
    // Ein Wechsel schreibt den gespeicherten Zugang in die Anmeldedaten von Jellyfin Web und lädt neu.
    var PROFILES_KEY = 'lrWeb.profiles';
    var PROFILE_PICKED_KEY = 'lrWeb.profilePicked';
    var CREDENTIALS_KEY = 'jellyfin_credentials';
    var PROFILE_COLORS = ['#e50914', '#2e96e0', '#7f76d6', '#f5a623', '#21a179', '#d94f9c', '#00a8a8', '#8a6d3b'];

    function profilesEnabled() {
        return !!(state.config && state.config.ProfilesEnabled);
    }

    function basePath() {
        var p = window.location.pathname;
        var i = p.indexOf('/web/');
        return i >= 0 ? p.slice(0, i) : '';
    }

    function readProfiles() {
        try {
            var list = JSON.parse(storageGet(PROFILES_KEY) || '[]');
            return Array.isArray(list) ? list.filter(function (p) {
                return p && p.Id && p.Token && p.ServerId;
            }) : [];
        } catch (e) {
            return [];
        }
    }

    function writeProfiles(list) {
        storageSet(PROFILES_KEY, list.length ? JSON.stringify(list) : null);
    }

    function readCredentials() {
        try {
            var credentials = JSON.parse(storageGet(CREDENTIALS_KEY) || '{}') || {};
            credentials.Servers = Array.isArray(credentials.Servers) ? credentials.Servers : [];
            return credentials;
        } catch (e) {
            return { Servers: [] };
        }
    }

    function signedInUserId(serverId) {
        var server = readCredentials().Servers.filter(function (s) {
            return s && s.Id === serverId;
        })[0];
        return server && server.AccessToken ? server.UserId : null;
    }

    function sameId(a, b) {
        return !!a && !!b && String(a).replace(/-/g, '').toLowerCase() === String(b).replace(/-/g, '').toLowerCase();
    }

    function isAdmin(user) {
        return !!(user && user.Policy && user.Policy.IsAdministrator);
    }

    function dropProfile(profile) {
        writeProfiles(readProfiles().filter(function (p) {
            return !(p.ServerId === profile.ServerId && sameId(p.Id, profile.Id));
        }));
    }

    // Entfernt ein Profil aus diesem Browser und meldet seinen gespeicherten Zugang auf dem Server ab
    // (nicht das gerade angemeldete Profil – das bleibt angemeldet, wird nur nicht mehr gemerkt)
    function removeProfile(profile) {
        dropProfile(profile);
        if (!sameId(signedInUserId(profile.ServerId), profile.Id)) {
            logoutToken(profile.Token);
        }
    }

    function logoutToken(token) {
        if (!token) {
            return;
        }
        try {
            // keepalive: die Anfrage überlebt das Neuladen beim Profilwechsel
            window.fetch(basePath() + '/Sessions/Logout', {
                method: 'POST',
                headers: { Authorization: 'MediaBrowser Token="' + token + '"' },
                cache: 'no-store',
                keepalive: true
            }).catch(function () {
                // Server nicht erreichbar – der Zugang ist hier trotzdem vergessen
            });
        } catch (e) {
            // ohne fetch nur vergessen
        }
    }

    // Beim Verlassen eines nicht gemerkten Zugangs (z. B. Administrator) diesen abmelden, statt ihn verwaist zu lassen
    function logoutUnremembered(server) {
        var token = server && server.AccessToken;
        if (token && !readProfiles().some(function (p) {
            return p.Token === token;
        })) {
            logoutToken(token);
        }
    }

    // Merkt sich das angemeldete Profil (Name, Bild, Zugang) für den Wechsel per Klick.
    // Administratoren nie: ihr Zugang soll nicht dauerhaft im Browser liegen, sie melden sich jedes Mal an.
    function rememberProfile() {
        var client = api();
        var uid = userId();
        if (!profilesEnabled() || !client || !uid || !client.accessToken || !client.accessToken()) {
            return;
        }
        var serverId = client.serverId ? client.serverId() : null;
        var token = client.accessToken();
        // Angemeldet in diesem Tab = Profil gewählt (z. B. kein erneutes „Wer schaut?“ nach dem Neuladen)
        try {
            window.sessionStorage.setItem(PROFILE_PICKED_KEY, '1');
        } catch (e) {
            // ohne Sitzungsspeicher erscheint die Auswahl eben öfter
        }
        client.getCurrentUser().then(function (user) {
            if (!user || !serverId) {
                return;
            }
            var list = readProfiles().filter(function (p) {
                return !(p.ServerId === serverId && sameId(p.Id, user.Id));
            });
            if (isAdmin(user)) {
                writeProfiles(list);
                return;
            }
            list.push({
                Id: user.Id,
                Name: user.Name,
                Tag: user.PrimaryImageTag || null,
                Token: token,
                ServerId: serverId,
                LastUsed: Date.now()
            });
            writeProfiles(list);
        }).catch(function () {
            // ohne Benutzerdaten nichts merken
        });
    }

    function profileAvatar(profile, size) {
        var box = el('span', 'lr-profile-avatar');
        var color = PROFILE_COLORS[Math.abs((profile.Name || '?').split('').reduce(function (h, c) {
            return (h * 31 + c.charCodeAt(0)) | 0;
        }, 7)) % PROFILE_COLORS.length];
        box.style.background = color;
        box.appendChild(el('span', 'lr-profile-initial', (profile.Name || '?').charAt(0).toUpperCase()));
        if (profile.Tag) {
            var img = new Image();
            img.alt = '';
            img.decoding = 'async';
            img.onload = function () {
                // Eigene (oft runde) Profilbilder ohne farbiges Quadrat dahinter
                box.style.background = 'transparent';
                box.textContent = '';
                box.appendChild(img);
            };
            img.src = basePath() + '/Users/' + encodeURIComponent(profile.Id) + '/Images/Primary?tag='
                + encodeURIComponent(profile.Tag) + '&maxHeight=' + size + '&quality=90';
        }
        return box;
    }

    // Prüft den gespeicherten Zugang (z. B. nach „Abmelden“ ist er serverseitig ungültig). Ein Administrator-Profil
    // aus älteren Versionen gilt ebenfalls als ungültig: es wird abgemeldet und muss sich neu anmelden.
    function checkToken(profile) {
        return window.fetch(basePath() + '/Users/Me', {
            headers: { Authorization: 'MediaBrowser Token="' + profile.Token + '"' },
            cache: 'no-store'
        }).then(function (response) {
            if (response.status === 401 || response.status === 403) {
                return false;
            }
            return response.json().then(function (user) {
                if (isAdmin(user)) {
                    removeProfile(profile);
                    return false;
                }
                return true;
            }, function () {
                return true;
            });
        }, function () {
            // Server nicht erreichbar – trotzdem versuchen, Jellyfin zeigt dann seine Meldung
            return true;
        });
    }

    function switchToProfile(profile) {
        var credentials = readCredentials();
        var server = credentials.Servers.filter(function (s) {
            return s && s.Id === profile.ServerId;
        })[0];
        if (!server) {
            server = { Id: profile.ServerId, ManualAddress: window.location.origin + basePath(), LastConnectionMode: 2 };
            credentials.Servers.push(server);
        }
        logoutUnremembered(server);
        server.UserId = profile.Id;
        server.AccessToken = profile.Token;
        server.DateLastAccessed = Date.now();
        storageSet(CREDENTIALS_KEY, JSON.stringify(credentials));
        // Sonst meldet Jellyfin Web trotz Zugang nicht automatisch an („Angemeldet bleiben“ war aus)
        storageSet('enableAutoLogin', 'true');
        var list = readProfiles();
        list.forEach(function (p) {
            if (p.ServerId === profile.ServerId && sameId(p.Id, profile.Id)) {
                p.LastUsed = Date.now();
            }
        });
        writeProfiles(list);
        window.location.replace(window.location.pathname + '#/home');
        window.location.reload();
    }

    // Neues Profil: nur hier im Browser abmelden (der Zugang der anderen bleibt gültig), dann zur Anmeldung
    function addProfile() {
        var credentials = readCredentials();
        credentials.Servers.forEach(function (s) {
            if (s) {
                logoutUnremembered(s);
                s.UserId = null;
                s.AccessToken = null;
            }
        });
        storageSet(CREDENTIALS_KEY, JSON.stringify(credentials));
        window.location.replace(window.location.pathname + '#/login');
        window.location.reload();
    }

    function closeProfilePicker() {
        var picker = document.querySelector('.lr-profiles');
        if (picker) {
            picker.remove();
        }
        document.documentElement.classList.remove('lr-profiles-open');
    }

    function showProfilePicker(options) {
        options = options || {};
        closeProfilePicker();
        try {
            window.sessionStorage.setItem(PROFILE_PICKED_KEY, '1');
        } catch (e) {
            // ohne Sitzungsspeicher erscheint die Auswahl eben öfter
        }
        var picker = el('div', 'lr-profiles');
        picker.setAttribute('role', 'dialog');
        picker.setAttribute('aria-modal', 'true');
        picker.setAttribute('aria-label', 'Wer schaut?');
        var inner = el('div', 'lr-profiles-inner');
        var title = el('h1', 'lr-profiles-title', 'Wer schaut?');
        var hint = el('p', 'lr-profiles-hint');
        var grid = el('div', 'lr-profiles-grid');
        var editing = !!options.manage;
        var manage;

        function draw() {
            grid.textContent = '';
            var list = readProfiles().sort(function (a, b) {
                return (a.Name || '').localeCompare(b.Name || '', 'de');
            });
            list.forEach(function (profile) {
                var current = sameId(signedInUserId(profile.ServerId), profile.Id);
                var tile = el('button', 'lr-profile' + (current ? ' lr-profile-current' : ''));
                tile.type = 'button';
                tile.appendChild(profileAvatar(profile, 320));
                if (editing) {
                    var remove = el('span', 'lr-profile-remove');
                    remove.appendChild(icon(avatarsEnabled() ? 'edit' : 'close'));
                    tile.appendChild(remove);
                }
                tile.appendChild(el('span', 'lr-profile-name', profile.Name));
                tile.setAttribute('aria-label', editing ? profile.Name + (avatarsEnabled() ? ' bearbeiten' : ' entfernen') : profile.Name);
                tile.addEventListener('click', function (e) {
                    e.preventDefault();
                    if (editing && avatarsEnabled()) {
                        openAvatarEditor({
                            profile: profile,
                            onDone: draw,
                            onRemove: function () {
                                removeProfile(profile);
                                draw();
                            }
                        });
                        return;
                    }
                    if (editing) {
                        removeProfile(profile);
                        draw();
                        return;
                    }
                    if (current && window.ApiClient && sameId(userId(), profile.Id)) {
                        closeProfilePicker();
                        return;
                    }
                    tile.classList.add('lr-profile-busy');
                    hint.textContent = '';
                    checkToken(profile).then(function (valid) {
                        tile.classList.remove('lr-profile-busy');
                        if (valid) {
                            switchToProfile(profile);
                            return;
                        }
                        dropProfile(profile);
                        hint.textContent = profile.Name + ' wurde abgemeldet – bitte über „Profil hinzufügen“ neu anmelden.';
                        draw();
                    });
                });
                grid.appendChild(tile);
            });
            if (!editing) {
                var add = el('button', 'lr-profile lr-profile-add');
                add.type = 'button';
                var plus = el('span', 'lr-profile-avatar');
                plus.appendChild(icon('add'));
                add.appendChild(plus);
                add.appendChild(el('span', 'lr-profile-name', 'Profil hinzufügen'));
                add.addEventListener('click', function (e) {
                    e.preventDefault();
                    addProfile();
                });
                grid.appendChild(add);
            }
            if (manage) {
                manage.textContent = editing ? 'Fertig' : 'Profile verwalten';
                manage.hidden = !list.length;
            }
            var first = grid.querySelector('.lr-profile-current') || grid.querySelector('.lr-profile');
            if (first && !options.noFocus) {
                first.focus();
            }
        }

        function drawTitle() {
            title.textContent = editing ? 'Profile verwalten' : 'Wer schaut?';
            hint.textContent = !editing ? '' : avatarsEnabled()
                ? 'Profil antippen, um das Profilbild zu ändern oder es aus diesem Browser zu entfernen (meldet es hier ab).'
                : 'Entfernt das Profil aus diesem Browser und meldet es hier ab.';
        }

        manage = el('button', 'lr-profiles-manage');
        manage.type = 'button';
        manage.addEventListener('click', function (e) {
            e.preventDefault();
            editing = !editing;
            drawTitle();
            draw();
        });
        drawTitle();
        inner.appendChild(title);
        inner.appendChild(grid);
        inner.appendChild(hint);
        inner.appendChild(manage);
        picker.appendChild(inner);
        picker.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && window.ApiClient && userId()) {
                e.stopPropagation();
                closeProfilePicker();
            }
        });
        document.documentElement.classList.add('lr-profiles-open');
        (document.body || document.documentElement).appendChild(picker);
        draw();
    }

    function accountMenuItem(iconName, label, handler) {
        var item = el('li', 'lr-menu-item lr-menu-account MuiButtonBase-root MuiMenuItem-root');
        item.setAttribute('role', 'menuitem');
        item.tabIndex = 0;
        var itemIcon = el('span', 'lr-menu-icon');
        if (iconName) {
            itemIcon.appendChild(icon(iconName));
        }
        item.appendChild(itemIcon);
        item.appendChild(el('span', null, label));
        if (handler) {
            onActivate(item, function () {
                closeUserMenu();
                window.setTimeout(handler, 150);
            });
        }
        return item;
    }

    // Was das Plugin über die eigenen Empfehlungen hinaus auswertet: Tageszeit, Suchen, geöffnete Titel – mit Schalter und Löschen
    function openBehaviorDialog() {
        var dialog = overlayShell('lr-calendar lr-behavior');
        var body = el('div', 'lr-cal-body');
        body.appendChild(el('h2', 'lr-cal-title', 'Was Quallenkino sich merkt'));
        body.appendChild(el('p', 'lr-cal-text', 'Empfehlungen entstehen aus dem, was du schaust: gesehene Titel (mehrfach Gesehenes zählt mehr, '
            + 'angefangene Filme etwas), Daumen und Favoriten, abgebrochene Filme, ignorierte Empfehlungen und Live-TV. '
            + 'Alles bleibt auf diesem Server.'));
        var box = el('div', 'lr-behavior-state');
        body.appendChild(box);
        var laterBox = el('div', 'lr-behavior-state');
        body.appendChild(laterBox);
        dialog.appendChild(body);
        var send = function (type, path, data) {
            return api().ajax({ type: type, url: url(path), data: data ? JSON.stringify(data) : undefined, contentType: data ? 'application/json' : undefined });
        };
        var orderNames = { movies: 'Filme', series: 'Serien', livetv: 'Live-TV' };
        var when = function (iso) {
            var d = new Date(iso);
            return d.toLocaleDateString('de-DE', { day: 'numeric', month: 'short' }) + ', ' + d.toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit' });
        };

        var render = function (st) {
            box.innerHTML = '';
            box.appendChild(el('h3', 'lr-behavior-heading', 'Tageszeit'));
            if (!st.DaypartEnabled) {
                box.appendChild(el('p', 'lr-cal-text', 'In den Einstellungen des Servers ausgeschaltet.'));
            } else if (st.PlaysInDaypart >= 15) {
                box.appendChild(el('p', 'lr-cal-text', 'Jetzt ist ' + st.Daypart + ' – um diese Zeit schaust du am ehesten: '
                    + st.Order.map(function (k) {
                        return orderNames[k] || k;
                    }).join(', ') + '. Danach richten sich die Reihen auf der Startseite.'));
            } else {
                box.appendChild(el('p', 'lr-cal-text', 'Für ' + st.Daypart + ' gibt es noch zu wenig Gesehenes (' + st.PlaysInDaypart + ' von 15) – die Reihen bleiben in der üblichen Reihenfolge.'));
            }

            box.appendChild(el('h3', 'lr-behavior-heading', 'Suchen und angesehene Titel'));
            if (!st.FeatureEnabled) {
                box.appendChild(el('p', 'lr-cal-text', 'In den Einstellungen des Servers ausgeschaltet.'));
                return;
            }
            var label = el('label', 'lr-trakt-toggle');
            var check = el('input');
            check.type = 'checkbox';
            check.checked = !!st.Enabled;
            check.addEventListener('change', function () {
                send('POST', 'LocalRecs/Behavior/Me', { Enabled: check.checked }).then(load);
            });
            label.appendChild(check);
            var days = st.RetentionDays || 365;
            label.appendChild(el('span', null, ' Suchen und geöffnete Titel merken (' + (days % 365 === 0 ? (days / 365 === 1 ? 'ein Jahr' : days / 365 + ' Jahre') : days + ' Tage') + ')'));
            box.appendChild(label);
            box.appendChild(el('p', 'lr-cal-hint', 'Ähnliche Titel rücken dann nach vorne („Weil du nach … gesucht hast“), Neues zählt mehr als Älteres. Ausschalten löscht alles.'));
            if (!st.Enabled) {
                return;
            }
            if (!st.Searches.length && !st.Views.length) {
                box.appendChild(el('p', 'lr-cal-text', 'Noch nichts gemerkt.'));
                return;
            }
            box.appendChild(el('p', 'lr-cal-text', st.Searches.length + ' Suchen, ' + st.Views.length + ' angesehene Titel gemerkt.'));
            var list = el('ul', 'lr-behavior-list');
            var fill = function (count) {
                list.innerHTML = '';
                st.Searches.slice(0, count).forEach(function (s) {
                    list.appendChild(el('li', null, 'Gesucht: „' + s.Term + '“ · ' + when(s.At)));
                });
                st.Views.slice(0, count).forEach(function (v) {
                    list.appendChild(el('li', null, 'Angesehen: „' + (v.Name || 'unbekannt') + '“ · ' + when(v.At)));
                });
            };
            fill(10);
            box.appendChild(list);
            if (st.Searches.length > 10 || st.Views.length > 10) {
                var more = button('lr-btn-link', 'expand_more', 'Alle anzeigen', function () {
                    fill(Infinity);
                    more.remove();
                });
                box.appendChild(more);
            }
            box.appendChild(button('lr-btn-link', 'delete_outline', 'Alles löschen', function () {
                send('DELETE', 'LocalRecs/Behavior/Me').then(load);
            }));
        };

        // Mit „Später“ aus dem Banner genommene Titel, einzeln zurückholbar
        var renderLater = function (entries) {
            laterBox.innerHTML = '';
            if (!entries || !entries.length) {
                return;
            }
            laterBox.appendChild(el('h3', 'lr-behavior-heading', 'Später ansehen (nicht im Banner)'));
            var list = el('ul', 'lr-behavior-list');
            entries.forEach(function (entry) {
                var li = el('li', null, '„' + (entry.Name || 'unbekannt') + '“ bis ' + new Date(entry.Until).toLocaleDateString('de-DE', { day: 'numeric', month: 'short' }) + ' ');
                li.appendChild(button('lr-btn-link', 'undo', 'Wieder zeigen', function () {
                    del('LocalRecs/Billboard/Later/' + entry.ItemId).then(load);
                }));
                list.appendChild(li);
            });
            laterBox.appendChild(list);
        };

        function load() {
            getJson('LocalRecs/Behavior/Me?utcOffsetMinutes=' + utcOffset()).then(render).catch(function () {
                box.innerHTML = '';
                box.appendChild(el('p', 'lr-cal-text', 'Der Stand konnte nicht geladen werden.'));
            });
            safe(getJson('LocalRecs/Billboard/Later')).then(renderLater);
        }

        load();
    }

    // Persönliche Trakt-Empfehlungen: Stand, ein/aus, eigene Verbindung per Code (Zugangsdaten bleiben auf dem Server)
    function openTraktDialog() {
        var dialog = overlayShell('lr-calendar lr-trakt');
        var body = el('div', 'lr-cal-body');
        body.appendChild(el('h2', 'lr-cal-title', 'Trakt-Empfehlungen'));
        body.appendChild(el('p', 'lr-cal-text', 'Was Trakt dir persönlich empfiehlt, fließt in deine Empfehlungen ein – nur Titel, die es hier gibt. '
            + 'Quallenkino ruft dafür nur deine Empfehlungsliste ab und schickt nichts an Trakt.'));
        var box = el('div', 'lr-trakt-state');
        body.appendChild(box);
        dialog.appendChild(body);
        var poll = null;
        var send = function (type, path, data) {
            return api().ajax({
                type: type,
                url: url(path),
                dataType: 'json',
                data: data ? JSON.stringify(data) : undefined,
                contentType: data ? 'application/json' : undefined
            });
        };

        var render = function (status) {
            box.innerHTML = '';
            window.clearTimeout(poll);
            if (!status.FeatureEnabled) {
                box.appendChild(el('p', 'lr-cal-text', 'In den Einstellungen des Servers ausgeschaltet.'));
                return;
            }
            var stateText = status.Source === 'eigen' ? 'Mit Trakt verbunden.'
                : status.Source === 'trakt-plugin' ? 'Verbunden über die Verknüpfung im Trakt-Plugin (nur lesend).'
                    : status.Enabled ? 'Eingeschaltet, aber kein Trakt-Konto gefunden.'
                        : 'Ausgeschaltet – es wird nichts bei Trakt abgerufen.';
            box.appendChild(el('p', 'lr-cal-text lr-trakt-status', stateText));
            if (status.Source || status.PluginInstalled || status.OwnConnected) {
                var label = el('label', 'lr-trakt-toggle');
                var check = el('input');
                check.type = 'checkbox';
                check.checked = !!status.Enabled;
                check.addEventListener('change', function () {
                    send('POST', 'LocalRecs/Trakt/Me', { Enabled: check.checked }).then(render);
                });
                label.appendChild(check);
                label.appendChild(el('span', null, ' Persönliche Trakt-Empfehlungen nutzen'));
                box.appendChild(label);
                if (!status.Enabled && !status.OwnConnected && status.PluginInstalled) {
                    box.appendChild(el('p', 'lr-cal-hint', 'Beim Einschalten nutzt Quallenkino – nur lesend – deine Verknüpfung im Trakt-Plugin, falls vorhanden.'));
                }
            }
            if (status.UserCode) {
                var codeBox = el('div', 'lr-trakt-code');
                codeBox.appendChild(el('p', 'lr-cal-text', 'Öffne die Adresse und gib den Code ein:'));
                var link = el('a', 'lr-btn lr-btn-info', status.VerificationUrl);
                link.href = status.VerificationUrl;
                link.target = '_blank';
                link.rel = 'noopener';
                codeBox.appendChild(link);
                codeBox.appendChild(el('div', 'lr-trakt-usercode', status.UserCode));
                box.appendChild(codeBox);
                poll = window.setTimeout(load, 4000);
            } else if (status.CanConnect && status.Source !== 'eigen') {
                box.appendChild(button('lr-btn-play', 'link', 'Mit Trakt verbinden', function () {
                    send('POST', 'LocalRecs/Trakt/Me/Login').then(render);
                }));
            }
            if (status.Source === 'eigen') {
                box.appendChild(button('lr-btn-link', 'link_off', 'Verbindung trennen', function () {
                    send('DELETE', 'LocalRecs/Trakt/Me/Login').then(render);
                }));
            }
            if (status.LoginError) {
                box.appendChild(el('p', 'lr-cal-hint', status.LoginError));
            }
            if (!status.Source && !status.CanConnect && !status.PluginInstalled) {
                box.appendChild(el('p', 'lr-cal-hint', 'Verknüpfe dein Konto im Trakt-Plugin (Dashboard → Plugins → Trakt) – oder der Admin hinterlegt eine eigene Trakt-App in den Quallenkino-Einstellungen.'));
            }
            box.appendChild(el('p', 'lr-cal-hint', 'Änderungen wirken bei der nächsten Aktualisierung der Empfehlungen.'));
        };

        function load() {
            if (!document.body.contains(dialog)) {
                return;
            }
            getJson('LocalRecs/Trakt/Me').then(render).catch(function () {
                box.innerHTML = '';
                box.appendChild(el('p', 'lr-cal-text', 'Der Stand konnte nicht geladen werden.'));
            });
        }

        load();
    }

    // Abmelden abgesetzt ganz unten
    function markLogout(list) {
        if (!list) {
            return;
        }
        list.querySelectorAll('.MuiMenuItem-root').forEach(function (item) {
            if (item.querySelector('[data-testid="LogoutIcon"]')) {
                item.classList.add('lr-menu-logout');
            }
        });
    }

    // ---------------------------------------------------------------- Profilbild aus Figur und Farbe

    // Einstellungen auch vor der Anmeldung (für „Wer schaut?“) aus dem Zwischenspeicher
    function webConfig() {
        if (state.config) {
            return state.config;
        }
        try {
            return JSON.parse(storageGet('lrWeb.config') || 'null') || {};
        } catch (e) {
            return {};
        }
    }

    function avatarsEnabled() {
        return !!webConfig().Avatars;
    }

    function avatarFetch(token, path, options) {
        options = options || {};
        var headers = { Authorization: 'MediaBrowser Token="' + token + '"' };
        if (options.body) {
            headers['Content-Type'] = 'application/json';
        }
        return window.fetch(basePath() + '/' + path, {
            method: options.method || 'GET',
            headers: headers,
            body: options.body ? JSON.stringify(options.body) : undefined,
            cache: 'no-store'
        }).then(function (response) {
            if (!response.ok) {
                var error = new Error('HTTP ' + response.status);
                error.status = response.status;
                throw error;
            }
            return response.json();
        });
    }

    // Vorschau wie das fertige Bild: Kreis in der Farbe, Figur darauf (bzw. randfüllend bei „cover“)
    function avatarPreview(className, iconEntry, color) {
        var box = el('span', 'lr-avatar-circle ' + (className || ''));
        box.style.background = color || '#4a4a55';
        if (iconEntry) {
            var img = el('img', iconEntry.Fit === 'cover' ? 'lr-avatar-cover' : null);
            img.alt = '';
            img.loading = 'lazy';
            img.src = basePath() + '/' + iconEntry.ImageUrl;
            box.appendChild(img);
        }
        return box;
    }

    function updateAvatarImages(id, tag) {
        var found = false;
        document.querySelectorAll('img').forEach(function (img) {
            var src = img.getAttribute('src') || '';
            if (src.indexOf('/Users/' + id + '/Images/Primary') >= 0 || src.indexOf('/Users/' + String(id).replace(/-/g, '') + '/Images/Primary') >= 0) {
                img.src = src.replace(/([?&]tag=)[^&]*/, '$1' + encodeURIComponent(tag));
                found = true;
            }
        });
        return found;
    }

    // options: { profile } für ein gespeichertes Profil aus „Wer schaut?“, sonst der angemeldete Benutzer
    function openAvatarEditor(options) {
        options = options || {};
        var profile = options.profile;
        var client = api();
        var token = profile ? profile.Token : (client && client.accessToken ? client.accessToken() : null);
        var id = profile ? profile.Id : userId();
        if (!token || !id) {
            return;
        }
        var old = document.querySelector('.lr-avatar-editor');
        if (old) {
            old.remove();
        }
        var root = el('div', 'lr-avatar-editor');
        root.setAttribute('role', 'dialog');
        root.setAttribute('aria-modal', 'true');
        root.setAttribute('aria-label', 'Profilbild wählen');
        var inner = el('div', 'lr-avatar-inner');
        var head = el('div', 'lr-avatar-head');
        var back = button('lr-round lr-avatar-back', 'arrow_back', null, close);
        back.setAttribute('aria-label', 'Zurück');
        head.appendChild(back);
        var titleBox = el('div', 'lr-avatar-titles');
        titleBox.appendChild(el('h1', 'lr-avatar-title', 'Profilbild wählen'));
        titleBox.appendChild(el('div', 'lr-avatar-sub', profile ? profile.Name : ''));
        head.appendChild(titleBox);
        inner.appendChild(head);
        var body = el('div', 'lr-avatar-body');
        body.appendChild(el('div', 'lr-avatar-status', 'Lade Figuren …'));
        inner.appendChild(body);
        root.appendChild(inner);
        root.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') {
                e.stopPropagation();
                close();
            }
        });
        (document.body || document.documentElement).appendChild(root);
        back.focus();

        function close() {
            root.remove();
        }

        if (!profile && client && client.getCurrentUser) {
            client.getCurrentUser().then(function (user) {
                titleBox.querySelector('.lr-avatar-sub').textContent = user && user.Name ? user.Name : '';
            }).catch(function () {
                return null;
            });
        }

        avatarFetch(token, 'LocalRecs/Avatars').then(function (catalog) {
            var all = {};
            (catalog.Categories || []).forEach(function (category) {
                (category.Icons || []).forEach(function (entry) {
                    all[entry.Id] = entry;
                });
            });
            var colors = catalog.Colors || [];
            var current = catalog.Current && all[catalog.Current.Icon] ? catalog.Current : null;
            var choice = {
                icon: current ? current.Icon : null,
                color: current && colors.indexOf(current.Color) >= 0 ? current.Color : colors[Math.floor(Math.random() * colors.length)]
            };
            var category = current ? current.Icon.split('/')[0] : ((catalog.Categories || [])[0] || {}).Id;
            body.textContent = '';

            var top = el('div', 'lr-avatar-top');
            var preview = el('div', 'lr-avatar-preview');
            top.appendChild(preview);
            var side = el('div', 'lr-avatar-side');
            side.appendChild(el('div', 'lr-avatar-label', 'Hintergrund'));
            var swatches = el('div', 'lr-avatar-colors');
            side.appendChild(swatches);
            var actions = el('div', 'lr-avatar-actions');
            var status = el('div', 'lr-avatar-status');
            var save = button('lr-btn-play lr-avatar-save', 'check', 'Speichern', onSave);
            actions.appendChild(save);
            if (options.onRemove) {
                actions.appendChild(button('lr-btn-info lr-avatar-remove', 'delete', 'Aus diesem Browser entfernen', function () {
                    close();
                    options.onRemove();
                }));
            }
            side.appendChild(actions);
            side.appendChild(status);
            top.appendChild(side);
            body.appendChild(top);

            var chips = el('div', 'lr-avatar-chips');
            body.appendChild(chips);
            var grid = el('div', 'lr-avatar-grid');
            body.appendChild(grid);
            if (catalog.License) {
                body.appendChild(el('div', 'lr-avatar-license', catalog.License));
            }

            function drawPreview() {
                preview.textContent = '';
                preview.appendChild(avatarPreview('lr-avatar-big', all[choice.icon], choice.color));
                save.disabled = !choice.icon;
            }

            function drawColors() {
                swatches.textContent = '';
                colors.forEach(function (color) {
                    var swatch = el('button', 'lr-avatar-swatch' + (color === choice.color ? ' lr-avatar-selected' : ''));
                    swatch.type = 'button';
                    swatch.style.background = color;
                    swatch.setAttribute('aria-label', 'Farbe ' + color);
                    swatch.addEventListener('click', function (e) {
                        e.preventDefault();
                        choice.color = color;
                        drawColors();
                        drawPreview();
                        drawGrid();
                    });
                    swatches.appendChild(swatch);
                });
            }

            function drawChips() {
                chips.textContent = '';
                (catalog.Categories || []).forEach(function (c) {
                    var chip = el('button', 'lr-avatar-chip' + (c.Id === category ? ' lr-avatar-selected' : ''), c.Name);
                    chip.type = 'button';
                    chip.addEventListener('click', function (e) {
                        e.preventDefault();
                        category = c.Id;
                        drawChips();
                        drawGrid();
                    });
                    chips.appendChild(chip);
                });
            }

            function drawGrid() {
                grid.textContent = '';
                var c = (catalog.Categories || []).filter(function (x) {
                    return x.Id === category;
                })[0];
                ((c && c.Icons) || []).forEach(function (entry) {
                    var tile = el('button', 'lr-avatar-tile' + (entry.Id === choice.icon ? ' lr-avatar-selected' : ''));
                    tile.type = 'button';
                    tile.title = entry.Name;
                    tile.setAttribute('aria-label', entry.Name);
                    tile.appendChild(avatarPreview(null, entry, choice.color));
                    tile.addEventListener('click', function (e) {
                        e.preventDefault();
                        choice.icon = entry.Id;
                        grid.querySelectorAll('.lr-avatar-selected').forEach(function (n) {
                            n.classList.remove('lr-avatar-selected');
                        });
                        tile.classList.add('lr-avatar-selected');
                        drawPreview();
                    });
                    grid.appendChild(tile);
                });
            }

            function onSave() {
                if (!choice.icon) {
                    return;
                }
                save.disabled = true;
                status.textContent = 'Wird gespeichert …';
                avatarFetch(token, 'LocalRecs/Avatars/Me', { method: 'POST', body: { Icon: choice.icon, Color: choice.color } }).then(function (result) {
                    var tag = result && result.PrimaryImageTag;
                    var list = readProfiles();
                    list.forEach(function (p) {
                        if (sameId(p.Id, id)) {
                            p.Tag = tag || p.Tag;
                        }
                    });
                    writeProfiles(list);
                    close();
                    if (options.onDone) {
                        options.onDone();
                    }
                    if (tag && sameId(userId(), id) && !updateAvatarImages(id, tag) && !profile) {
                        // Bisher ohne Bild (nur Anfangsbuchstabe) – neu laden, damit Jellyfin das Bild zeigt
                        window.location.reload();
                    }
                }).catch(function (error) {
                    save.disabled = false;
                    status.textContent = error && error.status === 401
                        ? 'Dieses Profil ist abgemeldet – bitte neu anmelden.'
                        : error && error.status === 403
                            ? 'Für dieses Profil ist das Ändern des Profilbilds nicht erlaubt.'
                            : 'Speichern hat nicht geklappt – bitte später noch einmal versuchen.';
                });
            }

            drawPreview();
            drawColors();
            drawChips();
            drawGrid();
        }).catch(function (error) {
            body.textContent = '';
            body.appendChild(el('div', 'lr-avatar-status', error && error.status === 401
                ? 'Dieses Profil ist abgemeldet – bitte neu anmelden.'
                : 'Die Figuren konnten nicht geladen werden.'));
        });
    }

    // Beim Öffnen der App (einmal pro Tab) die Auswahl zeigen – auch vor der Anmeldung, wenn Profile gespeichert sind
    function maybeShowProfilePicker(enabled) {
        var picked = null;
        try {
            picked = window.sessionStorage.getItem(PROFILE_PICKED_KEY);
        } catch (e) {
            picked = '1';
        }
        if (picked || !enabled || !readProfiles().length) {
            return;
        }
        showProfilePicker();
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

    // Dashboard „Live-TV“: jellyfin-web kennt nur XMLTV und Schedules Direct und zeigt jeden anderen Anbieter als
    // „Unknown“ mit dem Link „#/dashboard/livetv/undefined&id=…“ (Seite nicht gefunden). Für „LocalRecs EPG“ richtig
    // benennen und zu den Fernsehprogramm-Einstellungen des Plugins führen.
    var EPG_SETTINGS_ROUTE = '#/configurationpage?name=Quallenkino&tab=epg';
    var epgProviders = { ids: null, loading: false, observer: null };

    function isOwnEpgLink(href) {
        var match = /livetv\/undefined&id=([0-9a-f-]+)/i.exec(href || '');
        return !!(match && epgProviders.ids && epgProviders.ids.indexOf(match[1].replace(/-/g, '').toLowerCase()) >= 0);
    }

    function labelEpgProviders() {
        document.querySelectorAll('a[href*="livetv/undefined&id="]').forEach(function (link) {
            if (!isOwnEpgLink(link.getAttribute('href'))) {
                return;
            }
            link.setAttribute('href', EPG_SETTINGS_ROUTE);
            var title = link.querySelector('h1, h2, h3, h4, .MuiTypography-h3');
            if (title && title.textContent !== 'LocalRecs EPG (Quallenkino)') {
                title.textContent = 'LocalRecs EPG (Quallenkino)';
            }
        });
    }

    function fixEpgDashboard() {
        var hash = window.location.hash || '';
        var onList = /^#\/dashboard\/livetv\/?$/.test(hash);
        var onBrokenLink = /^#\/dashboard\/livetv\/undefined&id=/i.test(hash);
        if (!onList && !onBrokenLink) {
            if (epgProviders.observer) {
                epgProviders.observer.disconnect();
                epgProviders.observer = null;
            }
            return;
        }
        var apply = function () {
            if (onBrokenLink) {
                if (isOwnEpgLink(hash)) {
                    window.location.replace(EPG_SETTINGS_ROUTE);
                }
                return;
            }
            labelEpgProviders();
            if (!epgProviders.observer && window.MutationObserver) {
                epgProviders.observer = new MutationObserver(labelEpgProviders);
                epgProviders.observer.observe(document.body, { childList: true, subtree: true });
            }
        };
        if (epgProviders.ids) {
            apply();
            return;
        }
        if (epgProviders.loading || !api()) {
            return;
        }
        epgProviders.loading = true;
        getJson('System/Configuration/livetv').then(function (config) {
            epgProviders.ids = ((config && config.ListingProviders) || []).filter(function (p) {
                return p.Type === 'localrecs-epg' && p.Id;
            }).map(function (p) {
                return String(p.Id).replace(/-/g, '').toLowerCase();
            });
            epgProviders.loading = false;
            fixEpgDashboard();
        }).catch(function () {
            epgProviders.loading = false;
        });
    }

    // Klick vor React abfangen, damit der Router nicht die kaputte Adresse öffnet
    document.addEventListener('click', function (e) {
        var link = e.target && e.target.closest ? e.target.closest('a[href]') : null;
        if (link && link.getAttribute('href') === EPG_SETTINGS_ROUTE && /^#\/dashboard\/livetv/.test(window.location.hash || '')) {
            e.preventDefault();
            e.stopPropagation();
            window.location.hash = EPG_SETTINGS_ROUTE.slice(1);
        }
    }, true);

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
        maybeShowProfilePicker(!!(early && early.ProfilesEnabled));
    } catch (e) {
        // ignorieren
    }

    document.addEventListener('viewshow', onViewShow);
    window.setTimeout(fixEpgDashboard, 0);
    window.addEventListener('scroll', updateHeader, { passive: true });
    window.addEventListener('resize', hidePreview, { passive: true });
    window.addEventListener('hashchange', function () {
        window.setTimeout(fixEpgDashboard, 0);
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
            diagStart();
            lastUser = userId();
            rememberProfile();
            patchUpcomingPrograms();
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
                }).then(function () {
                    rememberProfile();
                    patchUpcomingPrograms();
                    activate();
                });
            }
        }, 2000);
    });
})();
