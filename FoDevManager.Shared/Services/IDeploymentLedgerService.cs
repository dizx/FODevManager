using FODevManager.Shared.Models;

namespace FODevManager.Services
{
    public interface IDeploymentLedgerService
    {
        IReadOnlyList<DeployedModelRecord> LoadRecords();

        bool CanDeploy(string profileName, string modelName, string sourcePath, out DeployedModelRecord? blocker);

        void RecordDeployment(DeployedModelRecord record);

        void RemoveDeployment(string modelName);

        void Clear();

        void SelfHeal();
    }
}
