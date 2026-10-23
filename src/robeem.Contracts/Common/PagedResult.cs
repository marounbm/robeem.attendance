namespace robeem.Contracts.Common;

/// <summary>One page of a list that is paged on the server, with the size of the whole list.</summary>
/// <typeparam name="T">The kind of item listed.</typeparam>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    /// <summary>Gets the number of items on every page together.</summary>
    public int Total { get; init; }

    /// <summary>Gets the 1-based page returned. It is the page asked for, or the last page when that is past the end.</summary>
    public int Page { get; init; } = 1;

    public int PageSize { get; init; }

    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);
}
