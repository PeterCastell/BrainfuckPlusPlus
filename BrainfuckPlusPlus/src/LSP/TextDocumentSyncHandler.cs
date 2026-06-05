// using MediatR;
// using OmniSharp.Extensions.LanguageServer.Protocol;
// using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
// using OmniSharp.Extensions.LanguageServer.Protocol.Document;
// using OmniSharp.Extensions.LanguageServer.Protocol.Models;
// using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

// namespace Brainfuck.LSP;
// public class TextDocumentSyncHandler : ITextDocumentSyncHandler
// {
//     public Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
//     {
//         FileCache.cache[request.TextDocument.Uri.ToString()] = request.TextDocument.Text;
//         return Unit.Task;
//     }

//     public Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
//     {
//         // with full sync, there's always exactly one change with the full text
//         FileCache.cache[request.TextDocument.Uri.ToString()] = request.ContentChanges.First().Text;
//         return Unit.Task;
//     }

//     public Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
//     {
//         FileCache.cache.Remove(request.TextDocument.Uri.ToString());
//         return Unit.Task;
//     }

//     public TextDocumentChangeRegistrationOptions GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
//     {
//         DocumentSelector = new TextDocumentSelector(
//             new TextDocumentFilter { Pattern = "**/*.bfpp" }
//         ),
//         SyncKind = TextDocumentSyncKind.Full
//     };

//     TextDocumentOpenRegistrationOptions IRegistration<TextDocumentOpenRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
//     {
//         DocumentSelector = new TextDocumentSelector(
//             new TextDocumentFilter { Pattern = "**/*.bfpp" }
//         )
//     };

//     TextDocumentCloseRegistrationOptions IRegistration<TextDocumentCloseRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
//     {
//         DocumentSelector = new TextDocumentSelector(
//             new TextDocumentFilter { Pattern = "**/*.bfpp" }
//         )
//     };

//     public Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Unit.Task;

//     TextDocumentSaveRegistrationOptions IRegistration<TextDocumentSaveRegistrationOptions, TextSynchronizationCapability>.GetRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities) => new()
//     {
//         DocumentSelector = new TextDocumentSelector(
//             new TextDocumentFilter { Pattern = "**/*.bfpp" }
//         ),
//         IncludeText = false
//     };
//     public TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "bfpp");
// }