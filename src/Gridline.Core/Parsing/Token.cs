namespace Gridline.Core.Parsing;

/// <summary>Lexical token categories (§5.1). <c>SheetPrefix</c> (CR1.2) carries the unescaped sheet name.</summary>
internal enum TokenKind { Number, String, Error, Function, CellRef, SheetPrefix, Bool, Name, Op, LParen, RParen, Comma, End }

/// <summary>A lexical token. <c>Text</c> semantics depend on <c>Kind</c> (see Tokenizer).</summary>
internal readonly record struct Token(TokenKind Kind, string Text, int Pos);
