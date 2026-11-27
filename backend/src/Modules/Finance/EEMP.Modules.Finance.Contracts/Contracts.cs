namespace EEMP.Modules.Finance.Contracts;

/// <summary>Finance facts other modules may act on (DL-065). No permission checks: callers authorize.</summary>
public interface IFinanceStanding
{
    /// <summary>True when the tenant blocks portal results on overdue balances and this student has an overdue issued balance.</summary>
    Task<bool> AreResultsBlockedAsync(Guid studentId, CancellationToken ct = default);
}

/// <summary>Deletion guard: whether finance records reference a student or branch.</summary>
public interface IFinanceUsage
{
    Task<bool> IsStudentReferencedAsync(Guid studentId, CancellationToken ct = default);
    Task<bool> IsBranchReferencedAsync(Guid branchId, CancellationToken ct = default);
}

public static class FinanceStandingErrors
{
    /// <summary>Same value as FinanceErrors.ResultsBlocked (Finance.Domain); exposed here for other modules.</summary>
    public const string ResultsBlocked = "finance.results_blocked";
    /// <summary>409 when deleting a student or branch that finance records reference (DL-085).</summary>
    public const string RecordsExist = "finance.records_exist";
}
