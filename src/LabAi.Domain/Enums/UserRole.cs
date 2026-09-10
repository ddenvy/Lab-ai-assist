namespace LabAi.Domain.Enums;

/// <summary>
/// Laboratory user roles controlling access to auditable actions. Mirrors the Mini-CDS enum so both
/// projects authorise the same vocabulary: an <see cref="Operator"/> may ask questions, an
/// <see cref="Analyst"/> may also ingest documents, an <see cref="Administrator"/> may read every
/// actor's audit rows.
/// </summary>
public enum UserRole
{
    Operator,
    Analyst,
    Administrator
}
