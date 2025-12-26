using System;

namespace Jellyfin.Plugin.LocalRecs.Api
{
    /// <summary>
    /// User library path information for setup UI.
    /// </summary>
    public class UserLibraryPathInfo
    {
        /// <summary>
        /// Gets or sets the user ID.
        /// </summary>
        public Guid UserId { get; set; }

        /// <summary>
        /// Gets or sets the username.
        /// </summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the movie library path.
        /// </summary>
        public string MovieLibraryPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the TV library path.
        /// </summary>
        public string TvLibraryPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the suggested movie library name.
        /// </summary>
        public string SuggestedMovieLibraryName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the suggested TV library name.
        /// </summary>
        public string SuggestedTvLibraryName { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the libraries are set up.
        /// </summary>
        public bool LibrariesCreated { get; set; }
    }
}
