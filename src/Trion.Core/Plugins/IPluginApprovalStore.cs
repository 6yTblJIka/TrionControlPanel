namespace Trion.Core.Plugins;

public interface IPluginApprovalStore
{
    bool   IsApproved(string hash);
    void   Approve(string hash, string notes = "");
    void   Revoke(string hash);
    IReadOnlyList<ApprovedPlugin> GetAll();
}
