const builtin = @import("builtin");
const std = @import("std");
const ffi = @import("ffi");

const Timestamp = std.Io.Timestamp;

const Cell = u8; //cell_type

const anyfunc = fn () callconv(.c) void;

const TapeAllocSize = 1 << 36;
const TapeLength = TapeAllocSize / @sizeOf(Cell);

var tapeMemory: [*]Cell = undefined;

var tapeCursor: usize = 0;

var lastTime: Timestamp = undefined;

var libMap: std.StringHashMap(std.DynLib) = undefined;

fn initMemory() !void {
    var basePtr: [*]align(std.heap.page_size_min) u8 = undefined;

    switch (builtin.os.tag) {
        .windows => {
            const windows = std.os.windows;

            const MEM_COMMIT = 0x00001000;
            const MEM_RESERVE = 0x00002000;
            const PAGE_READWRITE = 0x04;

            var basePtrRaw: **anyopaque = undefined;
            var tapeLengthRaw: usize = TapeAllocSize;
            const status = windows.ntdll.NtAllocateVirtualMemory(
                windows.GetCurrentProcess(),
                &basePtrRaw,
                0,
                &tapeLengthRaw,
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE,
            );
            if (status != .SUCCESS) {
                return error.AllocationFailed;
            }
            basePtr = @as([*]align(std.heap.page_size_min) u8, @ptrCast(@alignCast(basePtrRaw)));
        },
        else => basePtr = (try std.posix.mmap(
            null,
            TapeAllocSize,
            .{ .READ = true, .WRITE = true },
            .{ .TYPE = .PRIVATE, .ANONYMOUS = true },
            -1,
            0,
        )).ptr,
    }
    tapeMemory = @ptrCast(basePtr[0..]);
}

fn waitMs(delay: u64) void {
    const now = Timestamp.now() catch unreachable;
    const delayNs = delay * std.time.ns_per_ms;
    const elapsed = now.since(lastTime);
    if (delayNs > elapsed)
        std.Thread.sleep(delayNs - elapsed);
    lastTime = now;
}

fn tape() *Cell {
    return tapeAt(tapeCursor);
}

fn tapeAt(index: usize) *Cell {
    return &tapeMemory[index];
}

fn moveLeft(dist: usize) void {
    moveRight(TapeLength - (dist % TapeLength));
}

fn moveRight(dist: usize) void {
    tapeCursor = (tapeCursor + dist) % TapeLength;
}

fn whileCondition() bool {
    return tape().* != 0;
}

fn increase(amount: Cell) void {
    tape().* +%= amount;
}
fn decrease(amount: Cell) void {
    tape().* -%= amount;
}

fn increaseString(string: []const u8) void {
    writeTape(u8)(tapeCursor, string);
}
fn decreaseString(string: []const u8) void {
    writeTapeSub(u8)(tapeCursor, string);
}

fn takeReference() void {
    const ptr = &tapeMemory[tapeCursor];
    writeTape(u8)(tapeCursor, @as(*const [8]u8, @ptrCast(&ptr)));
}

fn dereference() void {
    var ptr: *const u8 = undefined;
    readTape(u8)(tapeCursor, @as(*[8]u8, @ptrCast(&ptr)));
    writeTape(u8)(tapeCursor + 8, @as(*const [1]u8, @ptrCast(ptr)));
}

fn read() !void {
    var char: [1]u8 = 0;
    _ = try std.fs.File.stdin().read(&char);
    tape().* = char[0];
}

fn print() !void {
    const char = [1]u8{@truncate(tape().*)};
    _ = try std.fs.File.stdout().write(&char);
}

fn assertSlowpath(cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    @branchHint(.cold);
    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d}); {s}\n", .{ file, lineNum, columnNum, tapeCursor, cursorPosition, msg });
    } else std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d})\n", .{ file, lineNum, columnNum, tapeCursor, cursorPosition });
    @panic("assert failed");
}

fn assert(cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (tapeCursor != cursorPosition) {
        if (comptime builtin.mode == .Debug or builtin.mode == .ReleaseSafe) {
            assertSlowpath(cursorPosition, lineNum, columnNum, file, message);
        } else {
            unreachable;
        }
    }
}

fn assertRelative(offset: isize, cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    const testPosition = (cursorPosition + @as(usize, @intCast(@mod(offset, TapeLength)))) % TapeLength;
    if (tapeCursor != testPosition) {
        if (comptime builtin.mode == .Debug or builtin.mode == .ReleaseSafe) {
            assertSlowpath(testPosition, lineNum, columnNum, file, message);
        } else {
            unreachable;
        }
    }
}

fn printDebugMessage(lineNum: i32, columnNum: i32, file: []const u8, message: []const u8) void {
    if (comptime builtin.mode != .Debug)
        return;
    std.debug.print("{s} ({d}, {d}): {s}\n", .{ file, lineNum, columnNum, message });
}

fn getDigitCount(num: usize) usize {
    if (num == 0) return 1;
    return std.math.log10(num) + 1;
}
fn getSignedDigitCount(num: usize) u64 {
    if (num == 0) return 1;
    const pos: isize = @bitCast(num % TapeLength);
    const spos = if (pos > TapeLength / 2) pos - TapeLength else pos;
    return std.math.log10(@abs(spos)) + 2;
}

fn getSignedTapePos(pos: usize) isize {
    const spos: isize = @bitCast(pos % TapeLength);
    return if (spos > TapeLength / 2) spos - TapeLength else spos;
}

fn printCell(lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): value at {d} is {d}; {s}\n", .{ file, lineNum, columnNum, getSignedTapePos(tapeCursor), tape().*, msg });
    } else std.debug.print("{s} ({d}, {d}): value at {d} is {d}\n", .{ file, lineNum, columnNum, getSignedTapePos(tapeCursor), tape().* });
}

fn printDump(length: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    const end = tapeCursor + length;

    if (message) |msg| {
        std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}; {s}\n|", .{ file, lineNum, columnNum, tapeCursor, end - 1, msg });
    } else std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}\n|", .{ file, lineNum, columnNum, tapeCursor, end - 1 });

    const entryWidth = @max(getSignedDigitCount(tapeCursor), getSignedDigitCount(end - 1), getDigitCount(std.math.maxInt(Cell)));

    for (tapeCursor..end) |i| {
        std.debug.print("{[value]d:<[width]}|", .{
            .value = getSignedTapePos(i),
            .width = entryWidth,
        });
    }
    std.debug.print("\n|", .{});

    for (tapeCursor..end) |i| {
        const pos = i % TapeLength;
        std.debug.print("{[value]d:<[width]}|", .{
            .value = tapeMemory[pos],
            .width = entryWidth,
        });
    }
    std.debug.print("\n", .{});
}

fn debugQuit(lineNum: i32, columnNum: i32, file: []const u8) void {
    if (comptime builtin.mode != .Debug)
        return;

    std.debug.print("{s} ({d}, {d}): quitting program\n", .{ file, lineNum, columnNum });
    @panic("debug quit");
}

/// Reads into buffer until end or zero byte is reached.
fn scanTape(comptime T: type, start: usize, buf: []T) []T {
    var len: usize = 0;
    for (0..buf.len) |i| {
        const cell = tapeAt((start + i) % TapeLength).*;
        if (cell == 0)
            return len;
        buf[len] = @truncate(cell);
        len += 1;
    }
    return buf[0..len];
}

/// Reads into buffer until end is reached
fn readTape(comptime T: type, start: usize, buf: []T) void {
    for (0..buf.len) |i| {
        const cell = tapeAt((start + i) % TapeLength).*;
        buf[i] = @truncate(cell);
    }
}

/// Adds the buffer onto the tape
fn writeTape(comptime T: type, start: usize, buf: []const T) void {
    for (0..buf.len) |i| {
        tapeAt((start + i) % TapeLength).* +%= @truncate(buf[i]);
    }
}

/// Subtracts the buffer onto the tape
fn writeTapeSub(comptime T: type, start: usize, buf: []const T) void {
    for (0..buf.len) |i| {
        tapeAt((start + i) % TapeLength).* -%= @truncate(buf[i]);
    }
}

fn getFFIType(size: u32) !*ffi.Type {
    const types = [_]*ffi.Type{
        ffi.types.void,
        ffi.types.uint8,
        ffi.types.sint8,
        ffi.types.uint16,
        ffi.types.sint16,
        ffi.types.uint32,
        ffi.types.sint32,
        ffi.types.uint64,
        ffi.types.sint64,
        ffi.types.float,
        ffi.types.double,
        ffi.types.pointer,
        ffi.types.long_double,
        ffi.types.complex_float,
        ffi.types.uchar,
        ffi.types.schar,
        ffi.types.ushort,
        ffi.types.sshort,
        ffi.types.uint,
        ffi.types.sint,
        ffi.types.ulong,
        ffi.types.long,
    };
    if (size > types.len)
        return error.NotAValidType;
    return types[size - 1];
}

const ExternCaller = struct {
    caller: ffi.Function,
    numParams: usize,
    paramTypes: [MaxParamCount]*ffi.Type,

    const MaxParamCount = 256;
};
var ExternFuncsCount: Cell = 0;
var ExternFuncs: [std.math.maxInt(Cell)]ExternCaller = undefined;

fn createExternCaller(line: u32, col: u32, file: []const u8) !void {
    if (ExternFuncsCount >= ExternFuncs.len)
        return error.ExternFuncsLimitReached;

    const MaxScanSize = ExternCaller.MaxParamCount;

    var typeSizesBuf: [MaxScanSize]u8 = @splat(0);
    const typeSizes = scanTape(u8, tapeCursor, &typeSizesBuf);

    var func = &ExternFuncs[ExternFuncsCount];

    if (typeSizes.len == 0)
        return error.ReturnTypeMissing;

    const numParams = typeSizes.len - 1;
    func.numParams = numParams;

    for (0..numParams) |i| {
        func.paramTypes[i] = getFFIType(typeSizes[i + 1]) catch |err| {
            std.debug.print("{s} ({d}, {d}): type index out of bounds for argument {d}\n", .{ file, line, col, i + 1 });
            return err;
        };
    }
    const returnType = getFFIType(typeSizes[0]) catch |err| {
        std.debug.print("{s} ({d}, {d}): type index out of bounds for return type\n", .{ file, line, col });
        return err;
    };

    func.caller.prepare(.default(), @intCast(numParams), func.paramTypes[0..], returnType) catch |err| {
        std.debug.print("{s} ({d}, {d}): failed to create exten function caller\n", .{ file, line, col });
        return err;
    };

    tapeAt(tapeCursor + numParams).* +%= ExternFuncsCount;

    ExternFuncsCount += 1;
}

fn findExternFunction(line: u32, col: u32, file: []const u8) !void {
    const MaxScanSize = 256;

    var dllNameBuf: [MaxScanSize + 1:0]u8 = @splat(0);
    var funcNameBuf: [MaxScanSize + 1:0]u8 = @splat(0);

    const dllNameSpan = scanTape(u8, tapeCursor, dllNameBuf[0..MaxScanSize]);
    const funcNameSpan = scanTape(u8, tapeCursor + 1 + dllNameSpan.len, funcNameBuf[0..MaxScanSize]);

    const dllName = dllNameBuf[0 .. dllNameSpan.len + 1];
    const funcName = funcNameBuf[0 .. funcNameSpan.len + 1];

    if (!libMap.contains(dllName))
        try libMap.put(dllName, std.DynLib.open(dllName) catch |err| {
            std.debug.print("{s} ({d}, {d}): couldn't open library \"{s}\"\n", .{ file, line, col, dllName });
            return err;
        });

    var lib = libMap.getPtr(dllName) orelse return error.LibNotFound;

    const func = lib.lookup(*const fn () callconv(.c) void, funcName) orelse {
        std.debug.print("{s} ({d}, {d}): couldn't find function \"{s}\"\n", .{ file, line, col, funcName });
        return error.FunctionNotFound;
    };

    writeTape(u8, tapeCursor + 2 + dllName.len + funcName.len, @as(*const [8]u8, @ptrCast(&func)));
}

fn callExternFunction() !void {
    if (tapeCursor > TapeLength - 256 * 8 + 1)
        return error.TooCloseToEndOfTape;

    const externIndex = tape().*;
    const func = &ExternFuncs[externIndex];

    var funcPtr: *anyfunc = undefined;
    var result: *anyopaque = undefined;
    var args: [256]*anyopaque = undefined;

    readTape(u8)(tapeCursor + 1, @as(*[8]u8, @ptrCast(&funcPtr)));

    readTape(u8)(tapeCursor + 9, @as(*[8]u8, @ptrCast(&result)));

    readTape(u8)(tapeCursor + 17, @as(*[256 * 8]u8, @ptrCast(&args)));

    func.caller.call(funcPtr, @ptrCast(args[0..]), result);
}

fn run() !void {
    // #
}

pub fn main(init: std.process.Init) !void {
    var alloc = if (builtin.mode == .Debug) std.heap.DebugAllocator(.{}).init;
    defer _ = if (builtin.mode == .Debug) alloc.deinit();

    const gpa = if (builtin.mode == .Debug) alloc.allocator() else std.heap.smp_allocator;

    libMap = std.StringHashMap(std.DynLib).init(gpa);
    defer libMap.deinit();

    lastTime = Timestamp.now(init.io, .real);

    try initMemory();
    try run();
}
