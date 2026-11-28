namespace EEMP.Modules.Consultation.Contracts;

/// <summary>Lets the Organization module know whether consultation records (profiles, slots, bookings, tickets) reference a branch.</summary>
public interface IConsultationBranchUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
}
