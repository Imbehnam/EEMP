namespace EEMP.SharedKernel.Domain;

/// <summary>Entities whose changes are not written to the generic audit trail (security tokens, audit rows).</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class NotAuditedAttribute : Attribute;
