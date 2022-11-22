namespace Valour.Api.Items.Messages.Embeds.Styles.Flex;

public struct FlexShrink : IStyle
{
    public int Value { get; set; }

    public FlexShrink(int value)
    {
        Value = value;
    }

    public override string ToString()
    {
        return $"flex-shrink: {Value};";
    }
}
