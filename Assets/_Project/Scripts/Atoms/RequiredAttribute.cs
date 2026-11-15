using System;

namespace Game.Atoms
{
    /// <summary>
    /// Marks a serialized field the component cannot work without. An empty required field (null object, an Atom
    /// Reference set to Constant/Variable/Instancer with nothing assigned, an Event Reference with no event, or an empty
    /// array) is reported by <c>RequiredFieldValidator</c>, blocks entering Play Mode, and fails the
    /// <c>RequiredFieldTests</c> project scan. Fields without this attribute are optional and must be null-checked.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
    public sealed class RequiredAttribute : Attribute
    {
    }
}
