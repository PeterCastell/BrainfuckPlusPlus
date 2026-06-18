const builtin = @import("builtin");
const std = @import("std");
const ffi = @import("ffi");

const Instant = std.time.Instant;

const cell = u8; //cell_type

fn cellSizeOf(T: type) u32 {
    return @sizeOf(T) / @sizeOf(cell);
}
fn CellSpan(T: type) type {
    return [cellSizeOf(T)]cell;
}

const IgnoreErrorFindExternFunction: bool = // #f
false; //    unreachable;
const IgnoreErrorCreateExternCaller: bool = // #c
false; //    unreachable;

const anyfunc = fn () callconv(.c) void;

const TapeAllocSize = 1 << 35;
const TapeLength = TapeAllocSize / @sizeOf(cell);

var tapeMemory: [*]cell = undefined;

fn initMemory() !void {
    var basePtr: [*]align(std.heap.page_size_min) u8 = undefined;

    switch (builtin.os.tag) {
        .windows => {
            const windows = std.os.windows;

            const MEM_COMMIT = 0x00001000;
            const MEM_RESERVE = 0x00002000;
            const PAGE_READWRITE = 0x04;

            var basePtrRaw: ?*anyopaque = null;
            var tapeLengthRaw: usize = TapeAllocSize;
            const status = windows.ntdll.NtAllocateVirtualMemory(
                windows.GetCurrentProcess(),
                @as(**anyopaque, @ptrCast(&basePtrRaw)),
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
        else => basePtr = try std.posix.mmap(
            null,
            TapeAllocSize,
            std.posix.PROT.READ | std.posix.PROT.WRITE,
            .{ .TYPE = .PRIVATE, .ANONYMOUS = true },
            -1,
            0,
        ),
    }
    tapeMemory = @ptrCast(basePtr[0..]);
}

const ExternCaller = struct {
    caller: ffi.Function,
    numParams: usize,
    paramTypes: [MaxParamCount]*ffi.Type,

    const MaxParamCount = 256;
};
var ExternFuncsCount: cell = 0;
var ExternFuncs: [std.math.maxInt(cell)]ExternCaller = undefined;

pub const ManagedThread = struct {
    thread: ?std.Thread = null,
    mutex: std.Thread.Mutex = .{},

    pub fn join(self: *ManagedThread) void {
        self.mutex.lock();
        defer self.mutex.unlock();

        if (self.thread) |t| {
            t.join();
            self.thread = null;
        }
    }

    pub fn start(self: *ManagedThread, func: anytype, id: cell) !void {
        self.mutex.lock();
        defer self.mutex.unlock();

        if (self.thread) |t| {
            t.detach();
        }

        self.thread = try std.Thread.spawn(.{}, func, .{id});
    }
};

var NextThreadIndex: cell = 0;
var Threads: [std.math.maxInt(cell)]ManagedThread = @splat(.{});

const Context = struct {
    tapeCursor: usize = 0,
    lastTime: Instant,

    pub fn init() !Context {
        return .{ .lastTime = try Instant.now() };
    }

    // === basic ops

    pub fn waitMs(self: *Context, delay: u64) void {
        const now = Instant.now() catch unreachable;
        const delayNs = delay * std.time.ns_per_ms;
        const elapsed = now.since(self.lastTime);
        if (delayNs > elapsed)
            std.Thread.sleep(delayNs - elapsed);
        self.lastTime = now;
    }

    pub fn resetLastTime(self: *Context) void {
        self.lastTime = Instant.now() catch unreachable;
    }

    pub fn tape(self: *Context) *cell {
        return tapeAt(self.tapeCursor);
    }

    pub fn tapeAt(index: usize) *cell {
        return &tapeMemory[index];
    }

    pub fn moveLeft(self: *Context, dist: usize) void {
        self.moveRight(TapeLength - (dist % TapeLength));
    }

    pub fn moveRight(self: *Context, dist: usize) void {
        self.tapeCursor = (self.tapeCursor + dist) % TapeLength;
    }

    pub fn whileCondition(self: *Context) bool {
        return self.tape().* != 0;
    }

    pub fn increase(self: *Context, amount: cell) void {
        self.tape().* +%= amount;
    }
    pub fn decrease(self: *Context, amount: cell) void {
        self.tape().* -%= amount;
    }
    fn increaseString(self: *Context, string: []const u8) void {
        writeTape(u8)(self.tapeCursor, string);
    }
    fn decreaseString(self: *Context, string: []const u8) void {
        writeTapeSub(u8)(self.tapeCursor, string);
    }

    pub fn takeReference(self: *Context) void {
        const ptr = &tapeMemory[self.tapeCursor];
        writeTapeValue(*const u8)(self.tapeCursor, ptr);
    }

    pub fn dereference(self: *Context) void {
        const ptr = readTapeValue(*const cell)(self.tapeCursor);
        writeTapeValue(cell)(self.tapeCursor + 8, ptr.*);
    }

    pub fn writeReference(self: *Context, embed: *const anyopaque) void {
        writeTapeValue(*const anyopaque)(self.tapeCursor, embed);
    }

    pub fn read(self: *Context) !void {
        var char: [1]u8 = 0;
        _ = try std.fs.File.stdin().read(&char);
        self.tape().* = char[0];
    }

    pub fn print(self: *Context) !void {
        const char = [1]u8{@truncate(self.tape().*)};
        _ = try std.fs.File.stdout().write(&char);
    }

    // === debug ops

    fn assertSlowpath(self: *Context, tapePosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        @branchHint(.cold);
        const sCursorPos = getSignedTapePos(self.tapeCursor);
        const sTapePos = getSignedTapePos(tapePosition);
        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d}): {s}\n", .{ file, lineNum, columnNum, sCursorPos, sTapePos, msg });
        } else std.debug.print("{s} ({d}, {d}): assert failed! ({d} != {d})\n", .{ file, lineNum, columnNum, sCursorPos, sTapePos });
        @panic("assert failed");
    }

    pub fn assert(self: *Context, tapePosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        if (self.tapeCursor != tapePosition) {
            if (comptime builtin.mode != .ReleaseFast) {
                self.assertSlowpath(tapePosition, lineNum, columnNum, file, message);
            } else {
                unreachable;
            }
        }
    }

    pub fn assertRelative(self: *Context, offset: isize, cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        const testPosition = (cursorPosition + @as(usize, @intCast(@mod(offset, TapeLength)))) % TapeLength;
        if (self.tapeCursor != testPosition) {
            if (comptime builtin.mode != .ReleaseFast) {
                self.assertSlowpath(testPosition, lineNum, columnNum, file, message);
            } else {
                unreachable;
            }
        }
    }

    pub fn printDebugMessage(self: *Context, lineNum: i32, columnNum: i32, file: []const u8, message: []const u8) void {
        _ = self;
        if (comptime builtin.mode != .Debug)
            return;
        std.debug.print("{s} ({d}, {d}): {s}\n", .{ file, lineNum, columnNum, message });
    }

    pub fn printCell(self: *Context, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        if (comptime builtin.mode != .Debug)
            return;

        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): value at {d} is {d}: {s}\n", .{ file, lineNum, columnNum, getSignedTapePos(self.tapeCursor), self.tape().*, msg });
        } else std.debug.print("{s} ({d}, {d}): value at {d} is {d}\n", .{ file, lineNum, columnNum, getSignedTapePos(self.tapeCursor), self.tape().* });
    }

    pub fn printDump(self: *Context, length: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        if (comptime builtin.mode != .Debug)
            return;

        const end = self.tapeCursor + length;

        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}: {s}\n|", .{ file, lineNum, columnNum, self.tapeCursor, end - 1, msg });
        } else std.debug.print("{s} ({d}, {d}): memory tape dump from {d} to {d}\n|", .{ file, lineNum, columnNum, self.tapeCursor, end - 1 });

        const entryWidth = @max(getSignedDigitCount(self.tapeCursor), getSignedDigitCount(end - 1), getDigitCount(std.math.maxInt(cell)));

        for (self.tapeCursor..end) |i| {
            std.debug.print("{[value]d:<[width]}|", .{
                .value = getSignedTapePos(i),
                .width = entryWidth,
            });
        }
        std.debug.print("\n|", .{});

        for (self.tapeCursor..end) |i| {
            const pos = i % TapeLength;
            std.debug.print("{[value]d:<[width]}|", .{
                .value = tapeMemory[pos],
                .width = entryWidth,
            });
        }
        std.debug.print("\n", .{});
    }

    pub fn debugQuit(self: *Context, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        _ = self;
        if (comptime builtin.mode == .ReleaseFast)
            return;

        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): quitting program: {s}\n", .{ file, lineNum, columnNum, msg });
        } else std.debug.print("{s} ({d}, {d}): quitting program\n", .{ file, lineNum, columnNum });

        @panic("debug quit");
    }

    // === extern ops

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
            ffi.types.uint,
            ffi.types.sint,
            ffi.types.ulong,
            ffi.types.long,
            ffi.types.long_double,
        };
        if (size > types.len)
            return error.NotAValidType;
        return types[size - 1];
    }

    fn createExternCaller(self: *Context, line: u32, col: u32, file: []const u8) !void {
        if (ExternFuncsCount >= ExternFuncs.len)
            return error.ExternFuncsLimitReached;

        const MaxScanSize = ExternCaller.MaxParamCount;

        var typeSizes = [_]u8{0} ** MaxScanSize;
        const typeSizesLen = scanTape(u8)(self.tapeCursor, typeSizes[0..]);

        var func = &ExternFuncs[ExternFuncsCount];

        if (typeSizesLen == 0)
            return error.ReturnTypeMissing;

        const numParams = typeSizesLen - 1;
        func.numParams = numParams;

        for (0..numParams) |i| {
            func.paramTypes[i] = getFFIType(typeSizes[i + 1]) catch |err| {
                if (comptime builtin.mode == .Debug)
                    std.debug.print("{s} ({d}, {d}): type index out of bounds for argument {d}\n", .{ file, line, col, i + 1 });
                if (IgnoreErrorFindExternFunction)
                    return;
                return err;
            };
        }
        const returnType = getFFIType(typeSizes[0]) catch |err| {
            if (comptime builtin.mode == .Debug)
                std.debug.print("{s} ({d}, {d}): type index out of bounds for return type\n", .{ file, line, col });
            if (IgnoreErrorCreateExternCaller)
                return;
            return err;
        };

        func.caller.prepare(.default(), @intCast(numParams), func.paramTypes[0..], returnType) catch |err| {
            if (comptime builtin.mode == .Debug)
                std.debug.print("{s} ({d}, {d}): failed to create exten function caller\n", .{ file, line, col });
            if (IgnoreErrorCreateExternCaller)
                return;
            return err;
        };

        tapeAt(self.tapeCursor + numParams + 1).* = ExternFuncsCount;

        ExternFuncsCount += 1;
    }

    fn findExternFunction(self: *Context, line: u32, col: u32, file: []const u8) !void {
        const MaxScanSize = 256;

        var dllName = [_]u8{0} ** (MaxScanSize + 1);
        var funcName = [_]u8{0} ** (MaxScanSize + 1);

        const dllNameLen = scanTape(u8)(self.tapeCursor, dllName[0..MaxScanSize]);
        const funcNameLen = scanTape(u8)(self.tapeCursor + dllNameLen + 1, funcName[0..MaxScanSize]);

        const dllNameSpan = dllName[0..dllNameLen];

        var lib = std.DynLib.open(dllNameSpan) catch |err| {
            if (comptime builtin.mode == .Debug)
                std.debug.print("{s} ({d}, {d}): couldn't open library \"{s}\"\n", .{ file, line, col, dllNameSpan });
            if (IgnoreErrorFindExternFunction)
                return;
            return err;
        };

        const func = lib.lookup(*const anyfunc, funcName[0..funcNameLen :0]) orelse {
            if (comptime builtin.mode == .Debug)
                std.debug.print("{s} ({d}, {d}): couldn't find function \"{s}\"\n", .{ file, line, col, funcName[0..funcNameLen] });
            if (IgnoreErrorFindExternFunction)
                return;
            return error.FunctionNotFound;
        };

        writeTapeValue(*const anyfunc)(self.tapeCursor + dllNameLen + 1 + funcNameLen + 1, func);
    }

    fn callExternFunction(self: *Context) !void {
        const externIndex = self.tape().*;
        const func = &ExternFuncs[externIndex];

        const funcPtr = readTapeValue(*anyfunc)(self.tapeCursor + 1);
        const result = readTapeValue(*anyopaque)(self.tapeCursor + 9);
        var args: [256]*anyopaque = undefined;

        readTape(u8)(self.tapeCursor + 17, @as(*[256 * 8]u8, @ptrCast(&args)));

        func.caller.call(funcPtr, @ptrCast(args[0..]), result);
    }

    // === threading ops

    pub fn spawnThread(self: *Context, func: anytype) !void {
        try Threads[NextThreadIndex].start(func, NextThreadIndex);
        writeTapeValue(cell)(self.tapeCursor, NextThreadIndex);
        NextThreadIndex += 1;
    }

    pub fn joinThread(self: *Context) !void {
        const index = readTapeValue(cell)(self.tapeCursor);
        Threads[index].join();
    }

    // === utils

    fn getDigitCount(num: usize) u32 {
        if (num == 0) return 1;
        return @intCast(std.math.log10(num) + 1);
    }
    fn getSignedDigitCount(num: usize) u32 {
        if (num == 0) return 1;
        const pos: isize = @bitCast(num % TapeLength);
        const spos = if (pos > TapeLength / 2) pos - TapeLength else pos;
        return @intCast(std.math.log10(@abs(spos)) + 2);
    }
    fn getSignedTapePos(pos: usize) isize {
        const spos: isize = @bitCast(pos % TapeLength);
        return if (spos > TapeLength / 2) spos - TapeLength else spos;
    }

    // reads into buffer until end or zero byte is reached
    fn scanTape(comptime T: type) fn (start: usize, buf: []T) usize {
        return struct {
            fn inner(start: usize, buf: []T) usize {
                var len: usize = 0;
                for (0..buf.len) |i| {
                    const cellVal = tapeAt((start + i) % TapeLength).*;
                    if (cellVal == 0)
                        return len;
                    buf[len] = @truncate(cellVal);
                    len += 1;
                }
                return len;
            }
        }.inner;
    }

    // reads into buffer until end is reached
    pub fn readTape(comptime T: type) fn (start: usize, buf: []T) void {
        return struct {
            fn inner(start: usize, buf: []T) void {
                for (0..buf.len) |i| {
                    const cellVal = tapeAt((start + i) % TapeLength).*;
                    buf[i] = @truncate(cellVal);
                }
            }
        }.inner;
    }

    // reads a single value from the tape
    pub fn readTapeValue(comptime T: type) fn (start: usize) T {
        return struct {
            fn inner(start: usize) T {
                var val: T = undefined;
                readTape(cell)(start, @as(*CellSpan(T), @ptrCast(&val)));
                return val;
            }
        }.inner;
    }

    // sets the tape tp the buffer's contents
    fn writeTape(comptime T: type) fn (start: usize, buf: []const T) void {
        return struct {
            fn inner(start: usize, buf: []const T) void {
                for (0..buf.len) |i| {
                    tapeAt((start + i) % TapeLength).* = @truncate(buf[i]);
                }
            }
        }.inner;
    }

    // writes a single value to the tape
    pub fn writeTapeValue(comptime T: type) fn (start: usize, val: T) void {
        return struct {
            fn inner(start: usize, val: T) void {
                writeTape(cell)(start, @as(*const CellSpan(T), @ptrCast(&val)));
            }
        }.inner;
    }

    // adds the buffer onto the tape
    fn writeTapeAdd(comptime T: type) fn (start: usize, buf: []const T) void {
        return struct {
            fn inner(start: usize, buf: []const T) void {
                for (0..buf.len) |i| {
                    tapeAt((start + i) % TapeLength).* +%= @truncate(buf[i]);
                }
            }
        }.inner;
    }
    // subtracts the buffer onto the tape
    fn writeTapeSub(comptime T: type) fn (start: usize, buf: []const T) void {
        return struct {
            fn inner(start: usize, buf: []const T) void {
                for (0..buf.len) |i| {
                    tapeAt((start + i) % TapeLength).* -%= @truncate(buf[i]);
                }
            }
        }.inner;
    }
};

fn functionFailed(err: anyerror) void {
    std.log.err("error: {s}", .{@errorName(err)});
    if (@errorReturnTrace()) |trace| {
        std.debug.dumpStackTrace(trace.*);
    }
    @panic("unhandled error");
}

fn discard(x: anytype) void {
    _ = x;
}

var mainArgs: [][:0]u8 = undefined;

// #g

fn run() !void {
    // #m
    var ctx = try Context.init();
    _ = &ctx;
    ctx.increaseString("Hello, World!");
    while(ctx.whileCondition()) {
        try ctx.print();
        ctx.moveRight(1);
    }

}

pub fn main() !void {
    mainArgs = try std.process.argsAlloc(std.heap.page_allocator);
    defer std.process.argsFree(std.heap.page_allocator, mainArgs);

    try initMemory();
    try run();
}
