namespace Gridline.Core.Parsing;

/// <summary>Lexical token categories (§5.1).</summary>
internal enum TokenKind { Number, String, Error, Function, CellRef, Bool, Name, Op, LParen, RParen, Comma, End }

/// <summary>A lexical token. <c>Text</c> semantics depend on <c>Kind</c> (see Tokenizer).</summary>
internal readonly record struct Token(TokenKind Kind, string Text, int Pos);
