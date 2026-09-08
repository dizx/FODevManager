using FODevManager.WinUI.ViewModel;

namespace FODevManager.Tests;

public class ModelSelectionTests
{
    private readonly List<string> _models = ["RepoA.Model1", "RepoA.Model2", "RepoB.Model1", "Standalone"];
    private ModelSelection<string> _selection = null!;

    [SetUp]
    public void SetUp() => _selection = new();

    [Test]
    public void ClickReplacesSelection()
    {
        Select(0);
        Select(2);
        Assert.That(_selection.Selected, Is.EqualTo(new[] { _models[2] }));
    }

    [TestCase(0, 3)]
    [TestCase(3, 0)]
    public void ShiftSelectsRangeAcrossRepositoriesAndStandaloneModels(int anchor, int end)
    {
        Select(anchor);
        Select(end, extend: true);
        Assert.That(_selection.Selected, Is.EquivalentTo(_models));
    }

    [Test]
    public void RepeatedShiftKeepsOriginalAnchor()
    {
        Select(1);
        Select(3, extend: true);
        Select(0, extend: true);
        Assert.That(_selection.Selected, Is.EquivalentTo(_models.Take(2)));
    }

    [Test]
    public void CtrlTogglesIndividualModels()
    {
        Select(0);
        Select(3, toggle: true);
        Assert.That(_selection.Selected, Has.Count.EqualTo(2));
        Select(0, toggle: true);
        Assert.That(_selection.Selected, Is.EqualTo(new[] { _models[3] }));
    }

    [Test]
    public void CtrlShiftAddsRangeToExistingSelection()
    {
        Select(0);
        Select(2, toggle: true);
        Select(3, extend: true, toggle: true);
        Assert.That(_selection.Selected, Is.EquivalentTo(new[] { _models[0], _models[2], _models[3] }));
    }

    [Test]
    public void ClearResetsSelectionAndRangeAnchor()
    {
        Select(0);
        _selection.Clear();
        Assert.That(_selection.Selected, Is.Empty);
        Select(3, extend: true);
        Assert.That(_selection.Selected, Is.EqualTo(new[] { _models[3] }));
    }

    [Test]
    public void RangeOnlyIncludesVisibleModels()
    {
        var visible = new List<string> { _models[0], _models[3] };
        _selection.Select(_models[0], visible, false, false);
        _selection.Select(_models[3], visible, true, false);
        Assert.That(_selection.Selected, Is.EquivalentTo(visible));
    }

    [Test]
    public void ShiftWithoutVisibleAnchorSelectsOnlyTarget()
    {
        Select(0);
        _selection.Select(_models[3], [_models[3]], true, false);
        Assert.That(_selection.Selected, Is.EqualTo(new[] { _models[3] }));
    }

    private void Select(int index, bool extend = false, bool toggle = false)
        => _selection.Select(_models[index], _models, extend, toggle);

    [Test]
    public void RefreshPreservesSelectionAndAnchorUsingReplacementInstances()
    {
        var selection = new ModelSelection<TestModel>();
        var original = new List<TestModel> { new("A"), new("B"), new("C") };
        var refreshed = new List<TestModel> { new("A"), new("B"), new("C") };
        selection.Select(original[0], original, false, false);
        selection.Select(original[1], original, true, false);

        selection.Reconcile(refreshed, (oldModel, newModel) => oldModel.Name == newModel.Name);

        Assert.That(selection.Selected, Is.EquivalentTo(refreshed.Take(2)));
        selection.Select(refreshed[2], refreshed, true, false);
        Assert.That(selection.Selected, Is.EquivalentTo(refreshed));
    }

    [Test]
    public void RefreshRemovesMissingModelsAndAnchor()
    {
        Select(0);
        _selection.Reconcile([_models[3]], (previous, current) => previous == current);
        Assert.That(_selection.Selected, Is.Empty);
        Select(3, extend: true);
        Assert.That(_selection.Selected, Is.EqualTo(new[] { _models[3] }));
    }

    private sealed class TestModel(string name)
    {
        public string Name { get; } = name;
    }
}
