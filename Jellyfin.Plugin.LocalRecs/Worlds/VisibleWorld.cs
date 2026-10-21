using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.LocalRecs.Worlds
{
    /// <summary>
    /// A world as a user sees it: visible members and the title for the tile image.
    /// </summary>
    public class VisibleWorld
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VisibleWorld"/> class.
        /// </summary>
        /// <param name="world">The world.</param>
        /// <param name="visible">Members visible to the user.</param>
        /// <param name="imageItemId">Best rated visible title with a backdrop, or null.</param>
        public VisibleWorld(WorldDefinition world, IReadOnlyList<Guid> visible, Guid? imageItemId)
        {
            World = world;
            Visible = visible;
            ImageItemId = imageItemId;
        }

        /// <summary>Gets the world.</summary>
        public WorldDefinition World { get; }

        /// <summary>Gets the members visible to the user.</summary>
        public IReadOnlyList<Guid> Visible { get; }

        /// <summary>Gets the title for the tile image.</summary>
        public Guid? ImageItemId { get; }
    }
}
