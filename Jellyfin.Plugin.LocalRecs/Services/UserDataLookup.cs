using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.LocalRecs.Services
{
    /// <summary>
    /// Lädt die Benutzerdaten (gesehen, Position, Daumen …) vieler Titel gesammelt statt einzeln – eine Datenbankabfrage
    /// je 500 Titel. Fehlt ein Titel in der Antwort, wird er einzeln nachgeladen.
    /// </summary>
    public sealed class UserDataLookup
    {
        private const int ChunkSize = 500;

        private readonly IUserDataManager _userDataManager;
        private readonly User _user;
        private readonly Dictionary<Guid, UserItemData> _loaded = new Dictionary<Guid, UserItemData>();

        private UserDataLookup(IUserDataManager userDataManager, User user)
        {
            _userDataManager = userDataManager;
            _user = user;
        }

        /// <summary>
        /// Loads the user data of the items in batches.
        /// </summary>
        /// <param name="userDataManager">User data manager.</param>
        /// <param name="user">The user.</param>
        /// <param name="items">The items.</param>
        /// <returns>The lookup.</returns>
        public static UserDataLookup Load(IUserDataManager userDataManager, User user, IEnumerable<BaseItem> items)
        {
            ArgumentNullException.ThrowIfNull(userDataManager);
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(items);
            var lookup = new UserDataLookup(userDataManager, user);
            foreach (var chunk in items.Where(i => i != null).DistinctBy(i => i.Id).Chunk(ChunkSize))
            {
                IReadOnlyDictionary<Guid, UserItemData>? batch = null;
                try
                {
                    batch = userDataManager.GetUserDataBatch(chunk, user);
                }
                catch (NotImplementedException)
                {
                    // ältere Server ohne gesammeltes Laden: einzeln
                }

                if (batch == null)
                {
                    continue;
                }

                foreach (var (id, data) in batch)
                {
                    if (data != null)
                    {
                        lookup._loaded[id] = data;
                    }
                }
            }

            return lookup;
        }

        /// <summary>
        /// Gets the user data of an item (from the batch, otherwise loaded on its own).
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>The user data, or null.</returns>
        public UserItemData? Get(BaseItem item)
        {
            ArgumentNullException.ThrowIfNull(item);
            return _loaded.TryGetValue(item.Id, out var data) ? data : _userDataManager.GetUserData(_user, item);
        }
    }
}
