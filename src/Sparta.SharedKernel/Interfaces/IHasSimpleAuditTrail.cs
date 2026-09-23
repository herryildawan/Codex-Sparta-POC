namespace Sparta.SharedKernel.Interfaces;

public interface IHasSimpleAuditTrail
{
    string? CreatedBy { get; }
    DateTime? CreatedAt { get; }
    string? UpdatedBy { get; }
    DateTime? UpdatedAt { get; }
}




