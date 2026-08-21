using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CloudflareR2Uploader.Models;

namespace CloudflareR2Uploader.Services
{
    public interface IUploadQueueLifecycle
    {
        bool IsRunning { get; }
        IReadOnlyList<UploadQueueItem> GetItems();
        Task StopAsync(bool preserveParts, CancellationToken cancellationToken);
    }
}
