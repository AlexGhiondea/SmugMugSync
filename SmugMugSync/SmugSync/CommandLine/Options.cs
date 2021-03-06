﻿using System.Collections.Generic;
using CommandLine.Attributes;
using CommandLine.Attributes.Advanced;

namespace SmugSync
{
    internal class Options

    {
        [ActionArgument]
        public CommandAction Action { get; set; }

        [ArgumentGroup(nameof(CommandAction.SyncAlbum))]
        [RequiredArgument(0, "albumId", "The id of the album to sync")]
        public string AlbumId { get; set; }


        [ArgumentGroup(nameof(CommandAction.SyncAlbum))]
        [RequiredArgument(1, "outputFolder", "The folder where to sync the album")]
        public string OutputFolder { get; set; }

        [ArgumentGroup(nameof(CommandAction.SyncAlbum))]
        [OptionalArgument(null, "tags", "Extra tags that the photos should have (at least one of them)", true)]
        public List<string> Tags { get; set; }
    }
}
