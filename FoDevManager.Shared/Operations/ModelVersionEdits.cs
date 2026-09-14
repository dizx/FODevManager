using FODevManager.Services;

namespace FODevManager.Operations;

public static class ModelVersionEdits
{
    public static ModelVersion Apply(ModelVersion original, ModelVersion edited, ModelVersion fresh) => new(
        original.Major != edited.Major ? edited.Major : fresh.Major,
        original.Minor != edited.Minor ? edited.Minor : fresh.Minor,
        original.Revision != edited.Revision ? edited.Revision : fresh.Revision);
}
