using FODevManager.Models;

namespace FODevManager.Operations;

public static class SelectedModelOperations
{
    public static void Run(ProfileModel freshProfile, IReadOnlyList<ProfileEnvironmentModel> selected,
        Func<ProfileEnvironmentModel, bool> action)
    {
        WorkflowContext.Require(selected.Count > 0, "Select at least one model");
        var targets = selected.Select(model =>
        {
            var fresh = WorkflowContext.Model(freshProfile, model.ModelName);
            if (fresh.ModelType != model.ModelType || fresh.MetadataFolder != model.MetadataFolder
                || fresh.CompiledModelFolder != model.CompiledModelFolder)
                throw new InvalidOperationException($"Selected model '{model.ModelName}' changed; refresh and select it again");
            return fresh;
        }).ToArray();

        var failures = new List<Exception>();
        foreach (var target in targets)
        {
            try { WorkflowContext.ServiceCall(() => WorkflowContext.Require(action(target), target.ModelName)); }
            catch (Exception exception) { failures.Add(new InvalidOperationException($"Selected model '{target.ModelName}' failed: {exception.Message}", exception)); }
        }
        if (failures.Count > 0) throw new AggregateException("Selected model operation failed", failures);
    }
}
