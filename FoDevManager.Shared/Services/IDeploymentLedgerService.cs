using FODevManager.Shared.Models;
using FODevManager.Models;

namespace FODevManager.Services
{
    public interface IDeploymentLedgerService
    {
        IReadOnlyList<DeployedModelRecord> LoadRecords();

        bool CanDeploy(string profileName, string modelName, string sourcePath, out DeployedModelRecord? blocker);

        bool IsModelDeployed(ProfileEnvironmentModel model);

        void RecordDeployment(DeployedModelRecord record);

        void RemoveDeployment(string modelName);

        void Clear();

        void SelfHeal();
    }
}
