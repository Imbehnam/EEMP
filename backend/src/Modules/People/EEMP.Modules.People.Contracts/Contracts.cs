namespace EEMP.Modules.People.Contracts;

/// <summary>Lets other modules know whether people records reference a branch.</summary>
public interface IPeopleBranchUsage
{
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
}
