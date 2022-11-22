namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public struct FlexOrder : IStyle
{
    public int Value { get; set; }

    public FlexOrder(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"order: {Value}";
    }
}
