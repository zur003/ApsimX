using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApsimNG.Cloud.Azure
{
    public static class AzureExtensions
    {
        /// <summary>
        /// List all blobs in a cloud storage container.
        /// </summary>
        /// <param name="container">Container whose blobs should be enumerated.</param>
        /// <param name="ct">Cancellation token.</param>
        public static async Task<List<BlobClient>> ListBlobsAsync(this BlobContainerClient container, CancellationToken ct)
        {
            List<BlobClient> results = new List<BlobClient>();
            await foreach (BlobItem blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, string.Empty))
            {
               results.Add(container.GetBlobClient(blob.Name));
            }
            return results;
        }
    }
}
