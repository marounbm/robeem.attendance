namespace robeem.Web.Models;

/// <summary>What the day does with two close punches at one end of it.</summary>
public enum PairKeeps
{
    /// <summary>The punches are left as they are: the day raises its exception.</summary>
    Nothing,

    /// <summary>The first counts and the second is left out.</summary>
    First,

    /// <summary>The second counts and the first is left out.</summary>
    Last,
}
