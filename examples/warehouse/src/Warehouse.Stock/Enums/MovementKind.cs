namespace Warehouse.Stock.Enums;

/// <summary>Why a stock level changed.</summary>
public enum MovementKind
{
    /// <summary>Goods arrived and were put on the shelf.</summary>
    Receipt,

    /// <summary>Goods were taken off the shelf for an order.</summary>
    Pick,

    /// <summary>A count found a difference, and somebody with the authority to say so corrected it.</summary>
    Adjustment
}
