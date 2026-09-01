using AssettoServer.Server.Fps;
using Microsoft.AspNetCore.Mvc;

namespace AssettoServer.Network.Http;

[ApiController]
public sealed class FpsAssetController : ControllerBase
{
    [HttpGet(FpsClientAssetArchive.Route)]
    [ResponseCache(Duration = 86_400, Location = ResponseCacheLocation.Any)]
    public IActionResult GetClientAssets()
    {
        return File(FpsClientAssetArchive.GetArchive(), "application/zip",
            FpsClientAssetArchive.FileName);
    }

    [HttpGet(FpsModernClientAssetArchive.Route)]
    [ResponseCache(Duration = 86_400, Location = ResponseCacheLocation.Any)]
    public IActionResult GetModernClientAssets()
    {
        return File(FpsModernClientAssetArchive.GetArchive(), "application/zip",
            FpsModernClientAssetArchive.FileName);
    }
}
