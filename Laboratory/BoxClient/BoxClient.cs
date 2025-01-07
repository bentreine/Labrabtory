using System.Collections.Concurrent;
using Box.Sdk.Gen;
using Box.Sdk.Gen.Managers;
using Box.Sdk.Gen.Schemas;
using Laboratory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Task = System.Threading.Tasks.Task;

namespace KellerPostman.MedicalRecords.Infrastructure.BoxWrapper;

public class BoxClient : IBoxClient
{
    private Box.Sdk.Gen.IBoxClient _adminGenClient;
    private readonly Dictionary<string, string> _folderCache = new();
    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<BoxClient> _logger;

    public BoxClient(
        IOptions<BoxClientOptions> options,
        IHostEnvironment hostEnvironment,
        ILogger<BoxClient> logger)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;

        //Set Developer Token
        var developerToken = "ToDOGetDeveloperToken";
        var auth = new BoxDeveloperTokenAuth(token: developerToken);
        _adminGenClient = new Box.Sdk.Gen.BoxClient(auth: auth);
    }

    /// <summary>
    /// Uploads the passed in medical records to box
    /// </summary>
    /// <param name="caseName">The case name which identifies the root Box folder these folders should go into</param>
    /// <param name="matterId">The folder, under the case name folder, these files should go into. This folder
    /// will be created if it doesn't already exist</param>
    /// <param name="injuredPartyName">The injured party name</param>
    /// <param name="filePaths">The list of corresponding files paths</param>
    public async Task UploadMedicalRecordsToBox(
        string caseName,
        string matterId,
        string injuredPartyName,
        List<(string TempFilePath, string SalesforceDocumentId)> filePaths,
        bool isAdditional = false)
    {
        var matterFolderId = "269800924207"; //CLJ ATTN ARCHER FolderId
        await UploadNewFiles(matterId, injuredPartyName, filePaths, matterFolderId);
    }

    #region Private Helpers
    /// <summary>
    /// Gets the root folder id for a given case name. If the
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the folder is not found</exception>
    /// <returns></returns>
    private async Task<string> GetCaseFolderId(string caseName)
    {
        //TODO If I'm uploading more than just CLJ files. I need to change this to be more dynamic
        return await GetFolderId(_rootFolderId, caseName, cacheFolderId: true, createFolder: false);
    }

    /// <summary>
    /// Gets the folder id for the Attention Archer folder
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the folder is not found</exception>
    private async Task<string> GetAttentionArcherFolderId(string caseName)
    {
        var caseFolderId = await GetCaseFolderId(caseName);
        return await GetFolderId(caseFolderId, AttentionArcherFolderName, cacheFolderId: true, createFolder: false);
    }

    /// <summary>
    /// Get's a folder id for a given folder name.
    /// </summary>
    /// <param name="rootFolderId">The parent folder to search through</param>
    /// <param name="folderName">The folder name to search for</param>
    /// <param name="cacheFolderId">If true, will cache the folder id so future queries do not hit the Box API</param>
    /// <param name="createFolder">If true, will create the folder if it isn't found</param>
    /// <exception cref="InvalidOperationException">Thrown when the folder is not found and createFolder is false</exception>
    /// <returns>The Folder id</returns>
    private async Task<string> GetFolderId(
        string rootFolderId,
        string folderName,
        bool cacheFolderId = false,
        bool createFolder = false)
    {
        // Return cache item, if possible
        if (_folderCache.ContainsKey($"{rootFolderId}:{folderName}"))
        {
            _logger.LogInformation("Caching exist for {rootFolderId}:{folderName}", rootFolderId, folderName);
            return _folderCache[$"{rootFolderId}:{folderName}"];
        }

        // Find the target folder
        FileFullOrFolderMiniOrWebLink? targetFolder = null;
        int entriesScanned = 0;
        int entriesInFolder = 0;
        string? marker = null;
        _logger.LogInformation("Searching for folder {folderName} in Box.", folderName);
        do
        {
            var rootFolders = await _adminGenClient.Folders.GetFolderItemsAsync(rootFolderId,
                new GetFolderItemsQueryParams { Limit = 1000, Marker = marker, Usemarker = true });

            _logger.LogInformation("Entries found with current api call: {entries}", rootFolders.Entries.Count);
            entriesScanned += rootFolders.Entries.Count;
            _logger.LogInformation("Total Entries scanned {entries}", entriesScanned);
            _logger.LogInformation($"Total entires in folder: {rootFolders.TotalCount}");

            marker = rootFolders.NextMarker;
            targetFolder = rootFolders.Entries.SingleOrDefault(f => string.Equals(f.FolderMini?.Name, folderName, StringComparison.OrdinalIgnoreCase));

            // Found the folder
            if (targetFolder != null)
            {
                _logger.LogInformation("Target Folder {TargetFolderId}:{TargetFolderName} found!", targetFolder.FolderMini?.Id, targetFolder.FolderMini?.Name);
                break;
            }
        } while (marker != null);

        if (targetFolder == null)
        {
            if (!createFolder)
            {
                throw new InvalidOperationException($"Folder {folderName} not found in {rootFolderId}");
            }

            // Create the target folder
            _logger.LogInformation("Creating new Box folder: {FolderName}", folderName);

            targetFolder = await _adminGenClient.Folders.CreateFolderAsync(
                new CreateFolderRequestBody(folderName,
                new CreateFolderRequestBodyParentField(rootFolderId)));

        }

        // Cache the folder
        if (cacheFolderId)
        {
            _logger.LogInformation("Adding Folder to cache: {FolderName} {FolderId}", folderName, targetFolder.FolderMini?.Id);
            _folderCache[$"{rootFolderId}:{folderName}"] = targetFolder.FolderMini!.Id;
        }

        return targetFolder.FolderMini!.Id;
    }

    private async Task<string> GetMatterFolder(string caseName, string matterId, string injuredPartyName, bool isAdditional = false)
    {
        var attentionArcherFolderId = await GetAttentionArcherFolderId(caseName);
        var folderName = isAdditional ? $"{injuredPartyName} - {matterId} addl" : $"{injuredPartyName} - {matterId}";
        var matterFolderId = await GetFolderId(attentionArcherFolderId, folderName, createFolder: true);
        return matterFolderId;
    }

    /// <summary>
    /// Uploads a file to a folder in Box. For large files, this method will use a session upload for better performance.
    /// </summary>
    /// <param name="FolderId">The Box folder id to upload to</param>
    /// <param name="FileName">The Box file name</param>
    /// <param name="fileRequest">The filestream of the file to upload to Box</param>
    /// <returns></returns>
    private async Task UploadFileToFolder((string FolderId, string FileName, string FilePath) fileRequest)
    {
        using var file = System.IO.File.OpenRead(fileRequest.FilePath);
        var fileName = Uri.EscapeDataString(fileRequest.FileName);
        var folderId = fileRequest.FolderId;

        try
        {
            // Progressive upload
            if (file.Length > 50_000_000)
            {
                await _adminGenClient.ChunkedUploads.UploadBigFileAsync(file, fileName, file.Length, folderId);
                return;
            }

            // Simple upload
            _logger.LogInformation("{FileName} being uploaded to Box", fileName);

            await _adminGenClient.Uploads.UploadFileAsync(new UploadFileRequestBody(
                new UploadFileRequestBodyAttributesField(fileName,
                new UploadFileRequestBodyAttributesParentField(folderId)),
                file));

        }
        catch (BoxApiException ex) when (ex.ResponseInfo.StatusCode == (int)System.Net.HttpStatusCode.Conflict)
        {
            // Log warning if filename already exists but allow process to continue on
            // We don't think that there are scenarios in which a med record would actually have changed in this case
            // and this would be mostly due to some calling code including previously sent record ids
            _logger.LogWarning(ex, "{FileName} upload conflict in Box", fileName);
        }
    }

    private async Task UploadNewFiles(string matterId, string injuredPartyName, List<(string TempFilePath, string SalesforceDocumentId)> files, string matterFolderId)
    {
        var distinctFiles = files.Distinct();
        var filesToUpload = distinctFiles.Select(file => 
            (FolderId: matterFolderId, 
            FileName: $"{injuredPartyName}_{matterId}_{file.SalesforceDocumentId}", 
            FilePath: file.TempFilePath));
        await Parallel.ForEachAsync(filesToUpload, async (file, _) => await UploadFileToFolder(file));
    }
    #endregion
}
