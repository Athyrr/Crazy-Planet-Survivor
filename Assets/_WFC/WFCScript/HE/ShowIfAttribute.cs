using System;
using UnityEngine;

namespace WFCContent.HE
{
    /// <summary>Shows a field when the named bool member matches the expected value.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ShowIfAttribute : PropertyAttribute
    {
        public string ConditionMember { get; }
        public bool ExpectedValue { get; }

        public ShowIfAttribute(string conditionMember, bool expectedValue = true)
        {
            ConditionMember = conditionMember;
            ExpectedValue = expectedValue;
        }
    }
}
