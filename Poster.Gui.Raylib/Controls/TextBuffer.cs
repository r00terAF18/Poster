namespace Poster.Gui.Controls;

/// <summary>Mutable text input state shared by single- and multi-line UI controls.</summary>
public sealed class TextBuffer(string value = "")
{
    public string Value { get; private set; } = value;
    public int Cursor { get; private set; } = value.Length;

    public void Set(string value)
    {
        Value = value;
        Cursor = value.Length;
    }

    public void Insert(char value)
    {
        Value = Value.Insert(Cursor, value.ToString());
        Cursor++;
    }

    public void Backspace()
    {
        if (Cursor == 0) return;
        Value = Value.Remove(Cursor - 1, 1);
        Cursor--;
    }

    public void Delete()
    {
        if (Cursor < Value.Length)
            Value = Value.Remove(Cursor, 1);
    }

    public void MoveLeft() => Cursor = Math.Max(0, Cursor - 1);
    public void MoveRight() => Cursor = Math.Min(Value.Length, Cursor + 1);

    public void MoveHome()
    {
        int searchIndex = Cursor == 0 ? -1 : Cursor - 1;
        Cursor = searchIndex < 0 ? 0 : Value.LastIndexOf('\n', searchIndex) + 1;
    }

    public void MoveEnd()
    {
        int newline = Value.IndexOf('\n', Cursor);
        Cursor = newline < 0 ? Value.Length : newline;
    }
}
