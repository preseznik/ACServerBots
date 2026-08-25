using System.Threading.Tasks;
using AssettoServer.Server.Configuration.Kunos;

namespace AssettoServer.Server.OpenSlotFilters;

public sealed class FpsSlotFilter : OpenSlotFilterBase
{
    public override async ValueTask<bool> IsSlotOpen(EntryCar entryCar, ulong guid)
    {
        if (entryCar.FpsRole == FpsSlotRole.Bot) return false;
        return await base.IsSlotOpen(entryCar, guid);
    }
}
