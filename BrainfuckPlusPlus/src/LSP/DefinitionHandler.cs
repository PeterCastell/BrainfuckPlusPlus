// namespace Brainfuck.LSP;

// using System.Text;
// using CommandLine;
// using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
// using OmniSharp.Extensions.LanguageServer.Protocol.Document;
// using OmniSharp.Extensions.LanguageServer.Protocol.Models;
// using OmniSharp.Extensions.LanguageServer.Protocol.Server;
// using OmniSharp.Extensions.LanguageServer.Protocol.Window;

// public class DefinitionHandler(ILanguageServerFacade server) : IDefinitionHandler
// {
//     void Log(string message) => server.Window.LogMessage(new()
//     {
//         Type = MessageType.Log,
//         Message = message
//     });
//     public Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken)
//     {
//         var uri = request.TextDocument.Uri.ToString();
//         var line = request.Position.Line;
//         var character = request.Position.Character;

//         // if (!_cache.TryGetFile(uri, out var text))
//         //     return Task.FromResult(new LocationOrLocationLinks());

//         // --- plug your compiler in here ---
//         // 1. parse `text` (or use already-parsed AST if you cache that too)
//         // 2. find the token at (line, character)
//         // 3. if it's a macro invocation, find where that macro is defined
//         //    (may require following $import chains using _cache.ReadFile(uri))
//         // 4. return the definition location
//         // ----------------------------------
//         // var path = new Uri(uri).AbsolutePath;

//         Log("Running Parser");

//         var error = new MemoryStream();
//         var ast = new Brainfuck.Parser(error).IncompleteParse(new Uri(uri).LocalPath);

//         if (ast is null)
//         {
//             Log("Error Length: " + error.Length);
//             Log(Encoding.UTF8.GetString(error.GetBuffer()));
//             return Task.FromResult(new LocationOrLocationLinks());
//         }
        
//         (TokenPosition start, TokenPosition stop)? Search(AST.Body body)
//         {
//             foreach(var token in body.Tokens)
//             {
//                 if (token.Token is Brainfuck.Parser.ASTMacroInvoke invoke)
//                 {
                    
//                 }
//             }
//         }

//         if (Search(ast.body) is (var start, var end))
//         {
//             return Task.FromResult(new LocationOrLocationLinks(new Location
//             {
//                 Uri = uri,
//                 Range = new Range(
//                     new Position(start.Row-1, start.Column-1),
//                     new Position(end.Row-1, end.Column-1)
//                 )
//             }));
//         }

//         return Task.FromResult(new LocationOrLocationLinks());
//     }

//     public DefinitionRegistrationOptions GetRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
//     {
//         return new DefinitionRegistrationOptions
//         {
//             DocumentSelector = TextDocumentSelector.ForPattern("**/*.bfpp")
//         };
//     }
// }