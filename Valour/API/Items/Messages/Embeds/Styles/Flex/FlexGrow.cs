namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public struct FlexGrow : IStyle
{
    public int Value { get; set; }

    public FlexGrow(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"flex-grow: {Value};";
    }
}
