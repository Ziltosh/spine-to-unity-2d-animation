using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpineTo2DAnimation.Editor.Reading
{
    /// <summary>
    /// Minimal JSON reader producing dictionaries, lists, doubles, strings, booleans and nulls.
    /// JsonUtility cannot read the dictionary-shaped parts of the Spine format, hence this reader.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            if (json == null)
                throw new ArgumentNullException(nameof(json));

            var parser = new Parser(json);
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd)
                throw parser.Error("Unexpected trailing content");

            return value;
        }

        private sealed class Parser
        {
            private readonly string text;
            private int index;

            public Parser(string text)
            {
                this.text = text;
                // Spine exports may start with a UTF-8 byte order mark.
                index = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
            }

            public bool AtEnd => index >= text.Length;

            public object ParseValue()
            {
                SkipWhitespace();
                if (AtEnd)
                    throw Error("Unexpected end of JSON");

                char current = text[index];
                switch (current)
                {
                    case '{':
                        return ParseObject();
                    case '[':
                        return ParseArray();
                    case '"':
                        return ParseString();
                    case 't':
                        ExpectLiteral("true");
                        return true;
                    case 'f':
                        ExpectLiteral("false");
                        return false;
                    case 'n':
                        ExpectLiteral("null");
                        return null;
                    default:
                        if (current == '-' || char.IsDigit(current))
                            return ParseNumber();
                        throw Error($"Unexpected character '{current}'");
                }
            }

            public void SkipWhitespace()
            {
                while (!AtEnd && char.IsWhiteSpace(text[index]))
                    index++;
            }

            public FormatException Error(string message)
            {
                return new FormatException($"{message} at offset {index}.");
            }

            private Dictionary<string, object> ParseObject()
            {
                var result = new Dictionary<string, object>(StringComparer.Ordinal);
                index++;
                SkipWhitespace();
                if (!AtEnd && text[index] == '}')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || text[index] != '"')
                        throw Error("Expected object key");

                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result[key] = ParseValue();
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unterminated object");

                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    Expect('}');
                    return result;
                }
            }

            private List<object> ParseArray()
            {
                var result = new List<object>();
                index++;
                SkipWhitespace();
                if (!AtEnd && text[index] == ']')
                {
                    index++;
                    return result;
                }

                while (true)
                {
                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (AtEnd)
                        throw Error("Unterminated array");

                    if (text[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    Expect(']');
                    return result;
                }
            }

            private string ParseString()
            {
                index++;
                var builder = new StringBuilder();
                while (!AtEnd)
                {
                    char current = text[index++];
                    if (current == '"')
                        return builder.ToString();

                    if (current != '\\')
                    {
                        builder.Append(current);
                        continue;
                    }

                    if (AtEnd)
                        break;

                    char escaped = text[index++];
                    switch (escaped)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            if (index + 4 > text.Length)
                                throw Error("Truncated unicode escape");
                            builder.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            index += 4;
                            break;
                        default:
                            throw Error($"Invalid escape '\\{escaped}'");
                    }
                }

                throw Error("Unterminated string");
            }

            private double ParseNumber()
            {
                int start = index;
                while (!AtEnd && "+-0123456789.eE".IndexOf(text[index]) >= 0)
                    index++;

                string token = text.Substring(start, index - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    throw Error($"Invalid number '{token}'");

                return value;
            }

            private void ExpectLiteral(string literal)
            {
                if (string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
                    throw Error($"Expected '{literal}'");

                index += literal.Length;
            }

            private void Expect(char expected)
            {
                if (AtEnd || text[index] != expected)
                    throw Error($"Expected '{expected}'");

                index++;
            }
        }
    }
}
