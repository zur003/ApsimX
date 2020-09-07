﻿namespace Models.Core
{
    using System.Collections.Generic;

    /// <summary>An interface for a model that references external files</summary>
    public interface IReferenceExternalFiles
    {
        /// <summary>Return paths to all files referenced by this model.</summary>
        IEnumerable<string> GetReferencedFileNames();

        /// <summary>Remove all paths from referenced filenames.</summary>
        void RemovePathsFromReferencedFileNames();
    }
}
