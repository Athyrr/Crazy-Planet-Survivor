using NUnit.Framework;
using UnityEngine;

public class DashSOResolveTests
{
    private DashSO _base;
    private DashSO _character;

    [SetUp]
    public void SetUp()
    {
        _base = ScriptableObject.CreateInstance<DashSO>();
        _base.Distance = new Overridable<float> { Override = true, Value = 6f };
        _base.DashDamage = new Overridable<float> { Override = true, Value = 0f };

        _character = ScriptableObject.CreateInstance<DashSO>();
        _character.BaseTemplate = _base;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_base);
        Object.DestroyImmediate(_character);
    }

    [Test]
    public void UnoverriddenField_ResolvesToBaseTemplateValue()
    {
        Assert.AreEqual(6f, _character.ResolvedDistance);
    }

    [Test]
    public void OverriddenField_ResolvesToOwnValue_NotBase()
    {
        _character.Distance = new Overridable<float> { Override = true, Value = 9f };
        Assert.AreEqual(9f, _character.ResolvedDistance);
    }

    [Test]
    public void ExplicitZeroOverride_ResolvesToZero_NotBaseTemplateValue()
    {
        // The whole reason Overridable<T> exists: DashDamage = 0 is a real "off" value, not "inherit".
        _base.DashDamage = new Overridable<float> { Override = true, Value = 5f };
        _character.DashDamage = new Overridable<float> { Override = true, Value = 0f };

        Assert.AreEqual(0f, _character.ResolvedDashDamage);
    }
}
