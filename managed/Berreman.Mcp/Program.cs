// Copyright (c) 2026 Javier Ruiz
// SPDX-License-Identifier: MIT

// Program.cs — stdio in, stdio out, and nothing on stdout that is not a message.
//
// One stray Console.WriteLine anywhere in this process corrupts the stream, so the entry point is
// this short and every diagnostic goes to stderr.

using Berreman.Mcp;

Console.OutputEncoding = System.Text.Encoding.UTF8;
new McpServer().Serve(Console.In, Console.Out);
return 0;
