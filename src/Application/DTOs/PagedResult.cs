namespace Application.DTOs;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount);
