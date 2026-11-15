# Public API changes, net10.0 -> net11.0

Watched namespaces: System.Net, System.IO, System.Data, System.Diagnostics, Microsoft.AspNetCore.Http, Microsoft.AspNetCore.Routing, Microsoft.AspNetCore.Builder. Catalog checked: net11.0 (113 rules).

495 members added, 0 removed. A member marked with a rule is already covered;
the others need review: add a rule to the delta catalog when the member is a side-effect, a request
source or a handler registration.

## Added (495)

### Microsoft.AspNetCore.Http.Connections.NegotiationResponse

- `get_TokenLifetime()`
- `set_TokenLifetime(System.Nullable`1<System.TimeSpan>)`

### Microsoft.AspNetCore.Http.Features.ITlsConnectionFeature

- `TryGetChannelBindingBytes(System.Security.Authentication.ExtendedProtection.ChannelBindingKind,System.ReadOnlyMemory`1<System.Byte>&)`

### System.Diagnostics.Activity

- `get_HasRandomizedTraceId()`

### System.Diagnostics.ActivityListener

- `RefreshSources()`

### System.Diagnostics.ActivitySource

- `Dispose(System.Boolean)`
- `get_Scope()`

### System.Diagnostics.ActivitySourceFactory

- `.ctor()`
- `Create(System.Diagnostics.ActivitySourceOptions)`
- `Create(System.String,System.String,System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.String,System.Object>>)`
- `CreateCore(System.Diagnostics.ActivitySourceOptions)`
- `Dispose()`
- `Dispose(System.Boolean)`

### System.Diagnostics.ActivitySourceOptions

- `get_Scope()`
- `set_Scope(System.Object)`

### System.Diagnostics.Process

- `ReadAllBytes(System.Nullable`1<System.TimeSpan>)`  — covered by process.control
- `ReadAllBytesAsync(System.Threading.CancellationToken)`  — covered by process.control
- `ReadAllLines(System.Nullable`1<System.TimeSpan>)`  — covered by process.control
- `ReadAllLinesAsync(System.Threading.CancellationToken)`  — covered by process.control
- `ReadAllText(System.Nullable`1<System.TimeSpan>)`  — covered by process.control
- `ReadAllTextAsync(System.Threading.CancellationToken)`  — covered by process.control
- `Run(System.Diagnostics.ProcessStartInfo,System.Nullable`1<System.TimeSpan>)`  — covered by process.run
- `Run(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Boolean,System.Nullable`1<System.TimeSpan>)`  — covered by process.run
- `RunAndCaptureText(System.Diagnostics.ProcessStartInfo,System.Nullable`1<System.TimeSpan>)`  — covered by process.run
- `RunAndCaptureText(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Nullable`1<System.TimeSpan>)`  — covered by process.run
- `RunAndCaptureTextAsync(System.Diagnostics.ProcessStartInfo,System.Threading.CancellationToken)`  — covered by process.run
- `RunAndCaptureTextAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Threading.CancellationToken)`  — covered by process.run
- `RunAsync(System.Diagnostics.ProcessStartInfo,System.Threading.CancellationToken)`  — covered by process.run
- `RunAsync(System.String,System.Collections.Generic.IEnumerable`1<System.String>,System.Boolean,System.Threading.CancellationToken)`  — covered by process.run
- `Signal(System.Runtime.InteropServices.PosixSignal)`  — covered by process.control
- `StartAndForget(System.Diagnostics.ProcessStartInfo)`  — covered by process.run
- `StartAndForget(System.String,System.Collections.Generic.IEnumerable`1<System.String>)`  — covered by process.run
- `TryGetProcessById(System.Int32,System.Diagnostics.Process&)`  — covered by process.control
- `TryWaitForExitStatus(System.TimeSpan,System.Diagnostics.ProcessExitStatus&)`  — covered by process.control
- `WaitForExitStatus()`  — covered by process.control
- `WaitForExitStatusAsync(System.Threading.CancellationToken)`  — covered by process.control

### System.Diagnostics.ProcessExitStatus

- `.ctor(System.Int32,System.Boolean,System.Nullable`1<System.Runtime.InteropServices.PosixSignal>)`
- `get_Canceled()`
- `get_ExitCode()`
- `get_Signal()`

### System.Diagnostics.ProcessOutputLine

- `.ctor(System.String,System.Boolean)`
- `ToString()`
- `get_Content()`
- `get_StandardError()`

### System.Diagnostics.ProcessStartInfo

- `get_InheritedHandles()`
- `get_KillOnParentExit()`
- `get_StandardErrorHandle()`
- `get_StandardInputHandle()`
- `get_StandardOutputHandle()`
- `get_StartDetached()`
- `get_StartSuspended()`
- `set_InheritedHandles(System.Collections.Generic.IList`1<System.Runtime.InteropServices.SafeHandle>)`
- `set_KillOnParentExit(System.Boolean)`
- `set_StandardErrorHandle(Microsoft.Win32.SafeHandles.SafeFileHandle)`
- `set_StandardInputHandle(Microsoft.Win32.SafeHandles.SafeFileHandle)`
- `set_StandardOutputHandle(Microsoft.Win32.SafeHandles.SafeFileHandle)`
- `set_StartDetached(System.Boolean)`
- `set_StartSuspended(System.Boolean)`

### System.Diagnostics.ProcessTextOutput

- `.ctor(System.Diagnostics.ProcessExitStatus,System.String,System.String,System.Int32)`
- `get_ExitStatus()`
- `get_ProcessId()`
- `get_StandardError()`
- `get_StandardOutput()`

### System.IO.Compression.BrotliCompressionOptions

- `get_WindowLog2()`
- `set_WindowLog2(System.Int32)`

### System.IO.Compression.DeflateDecoder

- `.ctor()`
- `Decompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `Dispose()`
- `Reset()`
- `TryDecompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`

### System.IO.Compression.DeflateEncoder

- `.ctor()`
- `.ctor(System.IO.Compression.ZLibCompressionOptions)`
- `.ctor(System.Int32)`
- `.ctor(System.Int32,System.Int32)`
- `Compress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&,System.Boolean)`
- `Dispose()`
- `Flush(System.Span`1<System.Byte>,System.Int32&)`
- `GetMaxCompressedLength(System.Int64)`
- `Reset()`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32,System.Int32)`

### System.IO.Compression.GZipDecoder

- `.ctor()`
- `Decompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `Dispose()`
- `Reset()`
- `TryDecompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`

### System.IO.Compression.GZipEncoder

- `.ctor()`
- `.ctor(System.IO.Compression.ZLibCompressionOptions)`
- `.ctor(System.Int32)`
- `.ctor(System.Int32,System.Int32)`
- `Compress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&,System.Boolean)`
- `Dispose()`
- `Flush(System.Span`1<System.Byte>,System.Int32&)`
- `GetMaxCompressedLength(System.Int64)`
- `Reset()`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32,System.Int32)`

### System.IO.Compression.ZLibCompressionOptions

- `get_DefaultWindowLog2()`
- `get_MaxWindowLog2()`
- `get_MinWindowLog2()`
- `get_WindowLog2()`
- `set_WindowLog2(System.Int32)`

### System.IO.Compression.ZLibDecoder

- `.ctor()`
- `Decompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `Dispose()`
- `Reset()`
- `TryDecompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`

### System.IO.Compression.ZLibEncoder

- `.ctor()`
- `.ctor(System.IO.Compression.ZLibCompressionOptions)`
- `.ctor(System.Int32)`
- `.ctor(System.Int32,System.Int32)`
- `Compress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&,System.Boolean)`
- `Dispose()`
- `Flush(System.Span`1<System.Byte>,System.Int32&)`
- `GetMaxCompressedLength(System.Int64)`
- `Reset()`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32,System.Int32)`

### System.IO.Compression.ZipArchive

- `CreateEntry(System.String,System.IO.Compression.CompressionLevel,System.ReadOnlySpan`1<System.Char>,System.IO.Compression.ZipEncryptionMethod)`
- `CreateEntry(System.String,System.ReadOnlySpan`1<System.Char>,System.IO.Compression.ZipEncryptionMethod)`

### System.IO.Compression.ZipArchiveEntry

- `Open(System.IO.FileAccess)`
- `Open(System.IO.FileAccess,System.ReadOnlySpan`1<System.Char>)`
- `Open(System.ReadOnlySpan`1<System.Char>)`
- `OpenAsync(System.IO.FileAccess,System.ReadOnlySpan`1<System.Char>,System.Threading.CancellationToken)`
- `OpenAsync(System.IO.FileAccess,System.Threading.CancellationToken)`
- `OpenAsync(System.ReadOnlySpan`1<System.Char>,System.Threading.CancellationToken)`
- `get_CompressionMethod()`
- `get_EncryptionMethod()`
- `get_VersionMadeBy()`

### System.IO.Compression.ZipExtractionOptions

- `.ctor()`
- `get_EntryNameEncoding()`
- `get_OverwriteFiles()`
- `get_Password()`
- `set_EntryNameEncoding(System.Text.Encoding)`
- `set_OverwriteFiles(System.Boolean)`
- `set_Password(System.ReadOnlyMemory`1<System.Char>)`

### System.IO.Compression.ZipFile

- `CreateFromDirectory(System.String,System.IO.Stream,System.IO.Compression.ZipFileCreationOptions)`  — covered by filesystem-write.zipfile
- `CreateFromDirectory(System.String,System.String,System.IO.Compression.ZipFileCreationOptions)`  — covered by filesystem-write.zipfile
- `CreateFromDirectoryAsync(System.String,System.IO.Stream,System.IO.Compression.ZipFileCreationOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile
- `CreateFromDirectoryAsync(System.String,System.String,System.IO.Compression.ZipFileCreationOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile
- `ExtractToDirectory(System.IO.Stream,System.String,System.IO.Compression.ZipExtractionOptions)`  — covered by filesystem-write.zipfile
- `ExtractToDirectory(System.String,System.String,System.IO.Compression.ZipExtractionOptions)`  — covered by filesystem-write.zipfile
- `ExtractToDirectoryAsync(System.IO.Stream,System.String,System.IO.Compression.ZipExtractionOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile
- `ExtractToDirectoryAsync(System.String,System.String,System.IO.Compression.ZipExtractionOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile

### System.IO.Compression.ZipFileCreationOptions

- `.ctor()`
- `get_CompressionLevel()`
- `get_EncryptionMethod()`
- `get_EntryNameEncoding()`
- `get_IncludeBaseDirectory()`
- `get_Password()`
- `set_CompressionLevel(System.IO.Compression.CompressionLevel)`
- `set_EncryptionMethod(System.IO.Compression.ZipEncryptionMethod)`
- `set_EntryNameEncoding(System.Text.Encoding)`
- `set_IncludeBaseDirectory(System.Boolean)`
- `set_Password(System.ReadOnlyMemory`1<System.Char>)`

### System.IO.Compression.ZipFileExtensions

- `CreateEntryFromFile(System.IO.Compression.ZipArchive,System.String,System.String,System.IO.Compression.CompressionLevel,System.ReadOnlySpan`1<System.Char>,System.IO.Compression.ZipEncryptionMethod)`  — covered by filesystem-write.zipfile-extensions
- `CreateEntryFromFile(System.IO.Compression.ZipArchive,System.String,System.String,System.ReadOnlySpan`1<System.Char>,System.IO.Compression.ZipEncryptionMethod)`  — covered by filesystem-write.zipfile-extensions
- `CreateEntryFromFileAsync(System.IO.Compression.ZipArchive,System.String,System.String,System.IO.Compression.CompressionLevel,System.ReadOnlyMemory`1<System.Char>,System.IO.Compression.ZipEncryptionMethod,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile-extensions
- `CreateEntryFromFileAsync(System.IO.Compression.ZipArchive,System.String,System.String,System.ReadOnlyMemory`1<System.Char>,System.IO.Compression.ZipEncryptionMethod,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile-extensions
- `ExtractToDirectory(System.IO.Compression.ZipArchive,System.String,System.IO.Compression.ZipExtractionOptions)`  — covered by filesystem-write.zipfile-extensions
- `ExtractToDirectoryAsync(System.IO.Compression.ZipArchive,System.String,System.IO.Compression.ZipExtractionOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile-extensions
- `ExtractToFile(System.IO.Compression.ZipArchiveEntry,System.String,System.IO.Compression.ZipExtractionOptions)`  — covered by filesystem-write.zipfile-extensions
- `ExtractToFileAsync(System.IO.Compression.ZipArchiveEntry,System.String,System.IO.Compression.ZipExtractionOptions,System.Threading.CancellationToken)`  — covered by filesystem-write.zipfile-extensions

### System.IO.Compression.ZstandardCompressionOptions

- `.ctor()`
- `get_AppendChecksum()`
- `get_DefaultQuality()`
- `get_DefaultWindowLog2()`
- `get_Dictionary()`
- `get_EnableLongDistanceMatching()`
- `get_MaxQuality()`
- `get_MaxWindowLog2()`
- `get_MinQuality()`
- `get_MinWindowLog2()`
- `get_Quality()`
- `get_TargetBlockSize()`
- `get_WindowLog2()`
- `set_AppendChecksum(System.Boolean)`
- `set_Dictionary(System.IO.Compression.ZstandardDictionary)`
- `set_EnableLongDistanceMatching(System.Boolean)`
- `set_Quality(System.Int32)`
- `set_TargetBlockSize(System.Int32)`
- `set_WindowLog2(System.Int32)`

### System.IO.Compression.ZstandardDecoder

- `.ctor()`
- `.ctor(System.IO.Compression.ZstandardDecompressionOptions)`
- `.ctor(System.IO.Compression.ZstandardDictionary)`
- `.ctor(System.IO.Compression.ZstandardDictionary,System.Int32)`
- `.ctor(System.Int32)`
- `Decompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `Dispose()`
- `Reset()`
- `SetPrefix(System.ReadOnlyMemory`1<System.Byte>)`
- `TryDecompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`
- `TryDecompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.IO.Compression.ZstandardDictionary)`
- `TryGetMaxDecompressedLength(System.ReadOnlySpan`1<System.Byte>,System.Int64&)`

### System.IO.Compression.ZstandardDecompressionOptions

- `.ctor()`
- `get_Dictionary()`
- `get_MaxWindowLog2()`
- `set_Dictionary(System.IO.Compression.ZstandardDictionary)`
- `set_MaxWindowLog2(System.Int32)`

### System.IO.Compression.ZstandardDictionary

- `Create(System.ReadOnlySpan`1<System.Byte>)`
- `Create(System.ReadOnlySpan`1<System.Byte>,System.Int32)`
- `Dispose()`
- `Train(System.ReadOnlySpan`1<System.Byte>,System.ReadOnlySpan`1<System.Int32>,System.Int32)`
- `get_Data()`

### System.IO.Compression.ZstandardEncoder

- `.ctor()`
- `.ctor(System.IO.Compression.ZstandardCompressionOptions)`
- `.ctor(System.IO.Compression.ZstandardDictionary)`
- `.ctor(System.IO.Compression.ZstandardDictionary,System.Int32)`
- `.ctor(System.Int32)`
- `.ctor(System.Int32,System.Int32)`
- `Compress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&,System.Boolean)`
- `Dispose()`
- `Flush(System.Span`1<System.Byte>,System.Int32&)`
- `GetMaxCompressedLength(System.Int64)`
- `Reset()`
- `SetPrefix(System.ReadOnlyMemory`1<System.Byte>)`
- `SetSourceLength(System.Int64)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.IO.Compression.ZstandardDictionary,System.Int32)`
- `TryCompress(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32,System.Int32)`

### System.IO.Compression.ZstandardStream

- `.ctor(System.IO.Stream,System.IO.Compression.CompressionLevel)`
- `.ctor(System.IO.Stream,System.IO.Compression.CompressionLevel,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.CompressionMode)`
- `.ctor(System.IO.Stream,System.IO.Compression.CompressionMode,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.CompressionMode,System.IO.Compression.ZstandardDictionary,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.ZstandardCompressionOptions,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.ZstandardDecoder,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.ZstandardDecompressionOptions,System.Boolean)`
- `.ctor(System.IO.Stream,System.IO.Compression.ZstandardEncoder,System.Boolean)`
- `BeginRead(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)`
- `BeginWrite(System.Byte[],System.Int32,System.Int32,System.AsyncCallback,System.Object)`
- `Dispose(System.Boolean)`
- `DisposeAsync()`
- `EndRead(System.IAsyncResult)`
- `EndWrite(System.IAsyncResult)`
- `Flush()`
- `FlushAsync(System.Threading.CancellationToken)`
- `Read(System.Byte[],System.Int32,System.Int32)`
- `Read(System.Span`1<System.Byte>)`
- `ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)`
- `ReadByte()`
- `Seek(System.Int64,System.IO.SeekOrigin)`
- `SetLength(System.Int64)`
- `SetSourceLength(System.Int64)`
- `Write(System.Byte[],System.Int32,System.Int32)`
- `Write(System.ReadOnlySpan`1<System.Byte>)`
- `WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)`
- `WriteByte(System.Byte)`
- `get_BaseStream()`
- `get_CanRead()`
- `get_CanSeek()`
- `get_CanWrite()`
- `get_Length()`
- `get_Position()`
- `set_Position(System.Int64)`

### System.IO.DirectoryNotFoundException

- `.ctor(System.String,System.String)`
- `.ctor(System.String,System.String,System.Exception)`
- `GetObjectData(System.Runtime.Serialization.SerializationInfo,System.Runtime.Serialization.StreamingContext)`
- `ToString()`
- `get_DirectoryPath()`

### System.IO.File

- `CreateHardLink(System.String,System.String)`  — covered by filesystem-write.hard-link
- `OpenNullHandle()`

### System.IO.FileInfo

- `CreateAsHardLink(System.String)`  — covered by filesystem-write.fileinfo-hard-link

### System.IO.Pipes.NamedPipeClientStream

- `.ctor(System.IO.Pipes.PipeDirection,System.Boolean,Microsoft.Win32.SafeHandles.SafePipeHandle)`

### System.IO.ReadOnlyMemoryStream

- `.ctor(System.ReadOnlyMemory`1<System.Byte>)`
- `CopyTo(System.IO.Stream,System.Int32)`
- `CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)`
- `Dispose(System.Boolean)`
- `Flush()`
- `FlushAsync(System.Threading.CancellationToken)`
- `Read(System.Byte[],System.Int32,System.Int32)`
- `Read(System.Span`1<System.Byte>)`
- `ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)`
- `ReadByte()`
- `Seek(System.Int64,System.IO.SeekOrigin)`
- `SetLength(System.Int64)`
- `Write(System.Byte[],System.Int32,System.Int32)`
- `Write(System.ReadOnlySpan`1<System.Byte>)`
- `WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)`
- `WriteByte(System.Byte)`
- `get_CanRead()`
- `get_CanSeek()`
- `get_CanWrite()`
- `get_Length()`
- `get_Position()`
- `set_Position(System.Int64)`

### System.IO.StringStream

- `.ctor(System.ReadOnlyMemory`1<System.Char>,System.Text.Encoding)`
- `.ctor(System.String,System.Text.Encoding)`
- `Dispose(System.Boolean)`
- `Flush()`
- `FlushAsync(System.Threading.CancellationToken)`
- `Read(System.Byte[],System.Int32,System.Int32)`
- `Read(System.Span`1<System.Byte>)`
- `ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)`
- `ReadByte()`
- `Seek(System.Int64,System.IO.SeekOrigin)`
- `SetLength(System.Int64)`
- `Write(System.Byte[],System.Int32,System.Int32)`
- `Write(System.ReadOnlySpan`1<System.Byte>)`
- `WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)`
- `get_CanRead()`
- `get_CanSeek()`
- `get_CanWrite()`
- `get_Encoding()`
- `get_Length()`
- `get_Position()`
- `set_Position(System.Int64)`

### System.IO.TextWriter

- `Write(System.Text.Rune)`
- `WriteAsync(System.String,System.Threading.CancellationToken)`
- `WriteAsync(System.Text.Rune)`
- `WriteLine(System.Text.Rune)`
- `WriteLineAsync(System.String,System.Threading.CancellationToken)`
- `WriteLineAsync(System.Text.Rune)`
- `WriteLineAsync(System.Threading.CancellationToken)`

### System.IO.WritableMemoryStream

- `.ctor(System.Memory`1<System.Byte>)`
- `CopyTo(System.IO.Stream,System.Int32)`
- `CopyToAsync(System.IO.Stream,System.Int32,System.Threading.CancellationToken)`
- `Dispose(System.Boolean)`
- `Flush()`
- `FlushAsync(System.Threading.CancellationToken)`
- `Read(System.Byte[],System.Int32,System.Int32)`
- `Read(System.Span`1<System.Byte>)`
- `ReadAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `ReadAsync(System.Memory`1<System.Byte>,System.Threading.CancellationToken)`
- `ReadByte()`
- `Seek(System.Int64,System.IO.SeekOrigin)`
- `SetLength(System.Int64)`
- `Write(System.Byte[],System.Int32,System.Int32)`
- `Write(System.ReadOnlySpan`1<System.Byte>)`
- `WriteAsync(System.Byte[],System.Int32,System.Int32,System.Threading.CancellationToken)`
- `WriteAsync(System.ReadOnlyMemory`1<System.Byte>,System.Threading.CancellationToken)`
- `WriteByte(System.Byte)`
- `get_CanRead()`
- `get_CanSeek()`
- `get_CanWrite()`
- `get_Length()`
- `get_Position()`
- `set_Position(System.Int64)`

### System.Net.AddressRecord

- `get_Address()`
- `get_Ttl()`

### System.Net.CNameRecord

- `get_CanonicalName()`
- `get_Ttl()`

### System.Net.Dns

- `ResolveAddresses(System.String)`  — covered by network.dns
- `ResolveAddresses(System.String,System.Net.Sockets.AddressFamily)`  — covered by network.dns
- `ResolveAddressesAsync(System.String,System.Net.Sockets.AddressFamily,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveAddressesAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveCName(System.String)`  — covered by network.dns
- `ResolveCNameAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveMx(System.String)`  — covered by network.dns
- `ResolveMxAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveNs(System.String)`  — covered by network.dns
- `ResolveNsAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolvePtr(System.Net.IPAddress)`  — covered by network.dns
- `ResolvePtr(System.String)`  — covered by network.dns
- `ResolvePtrAsync(System.Net.IPAddress,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolvePtrAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveSrv(System.String)`  — covered by network.dns
- `ResolveSrvAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns
- `ResolveTxt(System.String)`  — covered by network.dns
- `ResolveTxtAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns

### System.Net.DnsResolver

- `.ctor()`
- `.ctor(System.Net.DnsResolverOptions)`
- `Dispose()`
- `DisposeAsync()`
- `ResolveAddresses(System.String)`  — covered by network.dns-resolver
- `ResolveAddresses(System.String,System.Net.Sockets.AddressFamily)`  — covered by network.dns-resolver
- `ResolveAddressesAsync(System.String,System.Net.Sockets.AddressFamily,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveAddressesAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveCName(System.String)`  — covered by network.dns-resolver
- `ResolveCNameAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveMx(System.String)`  — covered by network.dns-resolver
- `ResolveMxAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveNs(System.String)`  — covered by network.dns-resolver
- `ResolveNsAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolvePtr(System.Net.IPAddress)`  — covered by network.dns-resolver
- `ResolvePtr(System.String)`  — covered by network.dns-resolver
- `ResolvePtrAsync(System.Net.IPAddress,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolvePtrAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveSrv(System.String)`  — covered by network.dns-resolver
- `ResolveSrvAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver
- `ResolveTxt(System.String)`  — covered by network.dns-resolver
- `ResolveTxtAsync(System.String,System.Threading.CancellationToken)`  — covered by network.dns-resolver

### System.Net.DnsResolverOptions

- `.ctor()`
- `get_Servers()`
- `set_Servers(System.Collections.Generic.IList`1<System.Net.IPEndPoint>)`

### System.Net.DnsResult`1

- `get_NegativeCacheTtl()`
- `get_Records()`
- `get_ResponseCode()`

### System.Net.Http.BrotliCompressedContent

- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.BrotliCompressionOptions)`
- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.CompressionLevel)`
- `Dispose(System.Boolean)`
- `SerializeToStream(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `TryComputeLength(System.Int64&)`

### System.Net.Http.GZipCompressedContent

- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.CompressionLevel)`
- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.ZLibCompressionOptions)`
- `Dispose(System.Boolean)`
- `SerializeToStream(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `TryComputeLength(System.Int64&)`

### System.Net.Http.HttpRequestMessage

- `get_ConnectionId()`
- `set_ConnectionId(System.Nullable`1<System.Int64>)`

### System.Net.Http.SocketsHttpConnectionContext

- `get_ConnectionId()`

### System.Net.Http.SocketsHttpConnectionEvictionContext

- `get_Age()`
- `get_ConnectionId()`
- `get_DnsEndPoint()`
- `get_HttpVersion()`
- `get_RemoteEndPoint()`

### System.Net.Http.SocketsHttpHandler

- `get_InitialHttp2MaxConcurrentStreams()`
- `get_ShouldEvictConnection()`
- `set_InitialHttp2MaxConcurrentStreams(System.Int32)`
- `set_ShouldEvictConnection(System.Func`3<System.Net.Http.SocketsHttpConnectionEvictionContext,System.Threading.CancellationToken,System.Threading.Tasks.Task`1<System.Boolean>>)`

### System.Net.Http.SocketsHttpPlaintextStreamFilterContext

- `get_ConnectionId()`

### System.Net.Http.ZstandardCompressedContent

- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.CompressionLevel)`
- `.ctor(System.Net.Http.HttpContent,System.IO.Compression.ZstandardCompressionOptions)`
- `Dispose(System.Boolean)`
- `SerializeToStream(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext)`
- `SerializeToStreamAsync(System.IO.Stream,System.Net.TransportContext,System.Threading.CancellationToken)`
- `TryComputeLength(System.Int64&)`

### System.Net.IPEndPoint

- `Parse(System.ReadOnlySpan`1<System.Byte>)`
- `TryFormat(System.Span`1<System.Byte>,System.Int32&)`
- `TryFormat(System.Span`1<System.Char>,System.Int32&)`
- `TryParse(System.ReadOnlySpan`1<System.Byte>,System.Net.IPEndPoint&)`

### System.Net.Mime.MediaTypeMap

- `GetExtension(System.ReadOnlySpan`1<System.Char>)`
- `GetExtension(System.String)`
- `GetMediaType(System.ReadOnlySpan`1<System.Char>)`
- `GetMediaType(System.String)`

### System.Net.MxRecord

- `get_Exchange()`
- `get_Preference()`
- `get_Ttl()`

### System.Net.NetworkInformation.NetworkChange

- `get_IsSupported()`

### System.Net.NsRecord

- `get_Name()`
- `get_Ttl()`

### System.Net.PtrRecord

- `get_Name()`
- `get_Ttl()`

### System.Net.Quic.QuicStream

- `get_Priority()`
- `set_Priority(System.Byte)`

### System.Net.Security.TlsBufferSession

- `.ctor()`
- `DrainPendingOutput(System.Span`1<System.Byte>,System.Int32&)`
- `Handshake(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `Read(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`
- `RequestClientCertificate(System.Span`1<System.Byte>,System.Int32&)`
- `Shutdown(System.Span`1<System.Byte>,System.Int32&)`
- `Write(System.ReadOnlySpan`1<System.Byte>,System.Span`1<System.Byte>,System.Int32&,System.Int32&)`

### System.Net.Security.TlsContext

- `CreateClient(System.Net.Security.SslClientAuthenticationOptions)`
- `CreateServer(System.Net.Security.SslServerAuthenticationOptions)`
- `Dispose()`

### System.Net.Security.TlsSession

- `AcceptWithDefaultValidation()`
- `Dispose()`
- `GetAcceptableIssuers()`
- `GetChannelBinding(System.Security.Authentication.ExtendedProtection.ChannelBindingKind)`
- `GetClientHelloLength()`
- `GetRemoteCertificate()`
- `GetRemoteCertificates()`
- `SetClientCertificateContext(System.Net.Security.SslStreamCertificateContext)`
- `SetContext(System.Net.Security.TlsContext)`
- `SetRemoteCertificateValidationResult(System.Net.Security.SslPolicyErrors)`
- `TryGetClientHelloBytes(System.Span`1<System.Byte>,System.Int32&)`
- `get_ClientHelloInfo()`
- `get_HasPendingOutput()`
- `get_IsHandshakeComplete()`
- `get_LocalCertificate()`
- `get_NegotiatedApplicationProtocol()`
- `get_NegotiatedCipherSuite()`
- `get_NegotiatedProtocol()`
- `get_TargetHostName()`
- `set_TargetHostName(System.String)`

### System.Net.Security.TlsSocketSession

- `.ctor(System.Net.Sockets.SafeSocketHandle)`
- `Handshake()`
- `Read(System.Span`1<System.Byte>,System.Int32&)`
- `RequestClientCertificate()`
- `Shutdown()`
- `Write(System.ReadOnlySpan`1<System.Byte>,System.Int32&)`
- `get_Socket()`

### System.Net.Sockets.Socket

- `ConnectAsync(System.Net.Sockets.SocketType,System.Net.Sockets.ProtocolType,System.Net.Sockets.SocketAsyncEventArgs,System.Net.Sockets.ConnectAlgorithm)`  — covered by network.socket

### System.Net.SrvRecord

- `get_Addresses()`
- `get_Port()`
- `get_Priority()`
- `get_Target()`
- `get_Ttl()`
- `get_Weight()`

### System.Net.TxtRecord

- `get_Ttl()`
- `get_Values()`

## Removed (0)

