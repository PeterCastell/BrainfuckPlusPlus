const builtin = @import("builtin");
const std = @import("std");
const ffi = @import("ffi");

const Io = std.Io;
const Mutex = std.Io.Mutex;
const Timestamp = std.Io.Timestamp;

fn cellSizeOf(T: type) u32 {
    return @max(1, @sizeOf(T) / @sizeOf(cell));
}
fn CellSpan(T: type) type {
    return [cellSizeOf(T)]cell;
}
fn ByteSpan(T: type) type {
    return [@sizeOf(T)]u8;
}
const ptrCellSize = cellSizeOf(*anyopaque);

fn includeCoreDebug() bool {
    return builtin.mode == .debug or builtin.mode == .release_safe;
}
fn includeAllDebug() bool {
    return builtin.mode == .debug;
}

const anyfunc = fn () callconv(.c) void;

pub fn IdMap(comptime T: type, comptime destroy: ?fn (val: *T) void) type {
    return if (comptime cell == u8) struct {
        data: [256]T,
        present: std.StaticBitSet(256),

        pub fn init(_: std.mem.Allocator) @This() {
            return .{ .data = undefined, .present = .empty };
        }

        pub fn ensure(self: *@This(), index: cell) !*T {
            if (self.present.isSet(index)) {
                if (destroy) |d| d(&self.data[index]);
            }
            self.present.set(index);
            return &self.data[index];
        }

        pub fn put(self: *@This(), index: cell, value: T) !void {
            const v = try self.ensure(index);
            v.* = value;
        }

        pub fn get(self: *@This(), index: cell) ?*T {
            if (!self.present.isSet(index)) return null;
            return &self.data[index];
        }
    } else struct {
        allocator: std.mem.Allocator,
        map: std.AutoHashMap(cell, *T),

        pub fn init(allocator: std.mem.Allocator) @This() {
            return .{ .allocator = allocator, .map = std.AutoHashMap(cell, *T).init(allocator) };
        }

        pub fn ensure(self: *@This(), index: cell) !*T {
            const res = try self.map.getOrPut(index);
            if (res.found_existing) {
                if (destroy) |d| d(res.value_ptr.*);
            } else {
                res.value_ptr.* = try self.allocator.create(T);
            }
            return res.value_ptr.*;
        }

        pub fn put(self: *@This(), index: cell, value: T) !void {
            const v = try self.ensure(index);
            v.* = value;
        }

        pub fn get(self: *@This(), index: cell) ?*T {
            return self.map.get(index);
        }
    };
}

const ExternFunc = struct {
    caller: ffi.Function,
    numParams: usize,
    paramTypes: [MaxParamCount]*ffi.Type,

    const MaxParamCount = 64;
};

pub const ManagedThread = struct {
    thread: ?std.Thread,

    pub fn start(func: anytype, id: cell) !ManagedThread {
        return .{ .thread = try std.Thread.spawn(.{}, func, .{id}) };
    }

    pub fn join(self: *ManagedThread) void {
        if (self.thread) |t| {
            t.join();
            self.thread = null;
        }
    }

    pub fn detach(self: *ManagedThread) void {
        if (self.thread) |t| {
            t.detach();
            self.thread = null;
        }
    }
};

const TapeAllocSize = 1 << 35;
const TapeLength = TapeAllocSize / @sizeOf(cell);

var tapeMemory: [*]cell = undefined;

var externFuncMap = IdMap(ExternFunc, null).init(gpa);
var threadMap = IdMap(ManagedThread, ManagedThread.detach).init(gpa);
var MutexMap = IdMap(Mutex, null).init(gpa);

fn initMemory() !void {
    var basePtr: [*]align(std.heap.page_size_min) u8 = undefined;

    switch (builtin.os.tag) {
        .windows => {
            const windows = std.os.windows;

            var basePtrRaw: *anyopaque = undefined;
            var tapeLengthRaw: usize = TapeAllocSize;
            const status = windows.ntdll.NtAllocateVirtualMemory(
                windows.GetCurrentProcess(),
                &basePtrRaw,
                0,
                &tapeLengthRaw,
                .{ .COMMIT = true, .RESERVE = true },
                .{ .READWRITE = true },
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

var rawModeEnabled = false;
fn enableRawMode(enabled: bool) void {
    rawModeEnabled = enabled;
    switch (comptime builtin.os.tag) {
        .windows => {
            const kernel32 = std.os.windows.kernel32;
            const DWORD = std.os.windows.DWORD;
            const handle = std.fs.File.stdin().handle;

            const ENABLE_LINE_INPUT: DWORD = 0x0002;
            const ENABLE_ECHO_INPUT: DWORD = 0x0004;

            const S = struct {
                var original: DWORD = 0;
                var saved: bool = false;
            };

            if (enabled and !S.saved) {
                if (kernel32.GetConsoleMode(handle, &S.original) == 0) return;
                S.saved = true;
                const raw = S.original & ~(ENABLE_LINE_INPUT | ENABLE_ECHO_INPUT);
                if (kernel32.SetConsoleMode(handle, raw) == 0) return;
            } else if (!enabled and S.saved) {
                if (kernel32.SetConsoleMode(handle, S.original) == 0) return;
                S.saved = false;
            }
        },
        else => {
            const posix = std.posix;
            const fd = std.fs.File.stdin().handle;

            const S = struct {
                var original: posix.termios = undefined;
                var saved: bool = false;
            };

            if (enabled and !S.saved) {
                S.original = posix.tcgetattr(fd) catch return;
                S.saved = true;
                var raw = S.original;
                raw.lflag.ICANON = false;
                raw.lflag.ECHO = false;
                raw.cc[std.meta.Tag(posix.V.MIN)] = 1;
                raw.cc[std.meta.Tag(posix.V.TIME)] = 0;
                posix.tcsetattr(fd, .FLUSH, raw) catch return;
            } else if (!enabled and S.saved) {
                posix.tcsetattr(fd, .FLUSH, S.original) catch return;
                S.saved = false;
            }
        },
    }
}

const Context = struct {
    tapeCursor: usize = 0,
    lastTime: Timestamp,

    pub fn init() !Context {
        return .{ .lastTime = Timestamp.now(io, .real) };
    }

    // === basic ops

    pub fn resetLastTime(self: *Context) void {
        self.lastTime = Timestamp.now(io, .real);
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
        writeTapeAdd(u8, self.tapeCursor, string);
    }
    fn decreaseString(self: *Context, string: []const u8) void {
        writeTapeSub(u8, self.tapeCursor, string);
    }

    pub fn takeReference(self: *Context) void {
        const ptr: *const anyopaque = @ptrCast(&tapeMemory[self.tapeCursor]);
        writeTapeValue(*const anyopaque, self.tapeCursor, ptr);
    }

    pub fn dereference(self: *Context) void {
        const ptr = readTapeValue(*const u8, self.tapeCursor);
        writeTapeValue(u8, self.tapeCursor + ptrCellSize, ptr.*);
    }

    pub fn writeReference(self: *Context, embed: *const anyopaque) void {
        writeTapeValue(*const anyopaque, self.tapeCursor, embed);
    }

    pub fn read(self: *Context) !void {
        const S = struct {
            var lastWasCR: bool = false;
        };
        var char: [1]u8 = @splat(0);
        while (true) {
            _ = try std.Io.File.stdin().readPositionalAll(io, &char, 0);
            if (char[0] == '\n' and S.lastWasCR) {
                S.lastWasCR = false;
                continue;
            }
            S.lastWasCR = char[0] == '\r';
            if (S.lastWasCR) char[0] = '\n';
            break;
        }
        self.tape().* = char[0];
    }

    pub fn print(self: *Context) !void {
        const char = [1]u8{@truncate(self.tape().*)};
        _ = try std.Io.File.stdout().writePositionalAll(io, &char, 0);
    }

    pub fn waitMs(self: *Context, delay: u64) void {
        const now = Timestamp.now(io, .real);
        const delayNs = delay * std.time.ns_per_ms;
        const elapsed = self.lastTime.durationTo(now).nanoseconds;
        if (delayNs > elapsed)
            io.sleep(std.Io.Duration.fromNanoseconds(delayNs - elapsed), .real) catch unreachable;
        self.lastTime = now;
    }

    pub fn setRawInput(self: *Context) !void {
        const enabled = self.tape().* == 0;
        enableRawMode(enabled);
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
            if (comptime includeCoreDebug()) {
                self.assertSlowpath(tapePosition, lineNum, columnNum, file, message);
            } else {
                unreachable;
            }
        }
    }

    pub fn assertRelative(self: *Context, offset: isize, cursorPosition: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        const testPosition = (cursorPosition + @as(usize, @intCast(@mod(offset, TapeLength)))) % TapeLength;
        if (self.tapeCursor != testPosition) {
            if (comptime includeCoreDebug()) {
                self.assertSlowpath(testPosition, lineNum, columnNum, file, message);
            } else {
                unreachable;
            }
        }
    }

    pub fn printDebugMessage(self: *Context, lineNum: i32, columnNum: i32, file: []const u8, message: []const u8) void {
        _ = self;
        if (comptime !includeAllDebug())
            return;
        std.debug.print("{s} ({d}, {d}): {s}\n", .{ file, lineNum, columnNum, message });
    }

    pub fn printCell(self: *Context, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        if (comptime !includeAllDebug())
            return;

        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): value at {d} is {d}: {s}\n", .{ file, lineNum, columnNum, getSignedTapePos(self.tapeCursor), self.tape().*, msg });
        } else std.debug.print("{s} ({d}, {d}): value at {d} is {d}\n", .{ file, lineNum, columnNum, getSignedTapePos(self.tapeCursor), self.tape().* });
    }

    pub fn printDump(self: *Context, length: usize, lineNum: i32, columnNum: i32, file: []const u8, message: ?[]const u8) void {
        if (comptime !includeAllDebug())
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
        if (comptime !includeCoreDebug())
            return;

        if (message) |msg| {
            std.debug.print("{s} ({d}, {d}): quitting program: {s}\n", .{ file, lineNum, columnNum, msg });
        } else std.debug.print("{s} ({d}, {d}): quitting program\n", .{ file, lineNum, columnNum });

        @panic("debug quit");
    }

    // === extern ops

    fn getIntFFIType(comptime intType: type) *ffi.Type {
        return switch (@typeInfo(intType).int.signedness) {
            .signed => switch (@bitSizeOf(intType)) {
                8 => ffi.types.sint8,
                16 => ffi.types.sint16,
                32 => ffi.types.sint32,
                64 => ffi.types.sint64,
                else => @compileError("unsupported integer width"),
            },
            .unsigned => switch (@bitSizeOf(intType)) {
                8 => ffi.types.uint8,
                16 => ffi.types.uint16,
                32 => ffi.types.uint32,
                64 => ffi.types.uint64,
                else => @compileError("unsupported integer width"),
            },
        };
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
            ffi.types.uint,
            ffi.types.sint,
            ffi.types.ulong,
            ffi.types.long,
            ffi.types.long_double,
            getIntFFIType(isize),
            getIntFFIType(usize),
            getIntFFIType(cell),
        };
        if (size > types.len)
            return error.NotAValidType;
        return types[size - 1];
    }

    fn createExternCaller(self: *Context, line: u32, col: u32, file: []const u8) !void {
        var func = try externFuncMap.ensure(self.tape().*);

        var typeSizesBuf: [ExternFunc.MaxParamCount]u8 = @splat(0);
        const typeSizes = scanTape(u8, self.tapeCursor + 1, &typeSizesBuf);

        if (typeSizes.len == 0)
            return error.ReturnTypeMissing;

        const numParams = typeSizes.len - 1;
        func.numParams = numParams;

        for (0..numParams) |i| {
            func.paramTypes[i] = getFFIType(typeSizes[i + 1]) catch |err| {
                if (comptime !includeAllDebug())
                    std.debug.print("{s} ({d}, {d}): type index out of bounds for argument {d}\n", .{ file, line, col, i + 1 });
                if (IgnoreErrorFindExternFunction)
                    return;
                return err;
            };
        }
        const returnType = getFFIType(typeSizes[0]) catch |err| {
            if (comptime !includeAllDebug())
                std.debug.print("{s} ({d}, {d}): type index out of bounds for return type\n", .{ file, line, col });
            if (IgnoreErrorCreateExternCaller)
                return;
            return err;
        };

        func.caller.prepare(.default(), @intCast(numParams), func.paramTypes[0..], returnType) catch |err| {
            if (comptime !includeAllDebug())
                std.debug.print("{s} ({d}, {d}): failed to create exten function caller\n", .{ file, line, col });
            if (IgnoreErrorCreateExternCaller)
                return;

            return err;
        };
    }

    fn findExternFunction(self: *Context, line: u32, col: u32, file: []const u8) !void {
        const MaxScanSize = 256;

        var dllNameBuf: [MaxScanSize + 1:0]u8 = @splat(0);
        var funcNameBuf: [MaxScanSize + 1:0]u8 = @splat(0);

        const dllNameSpan = scanTape(u8, self.tapeCursor, dllNameBuf[0..MaxScanSize]);
        const funcNameSpan = scanTape(u8, self.tapeCursor + 1 + dllNameSpan.len, funcNameBuf[0..MaxScanSize]);

        const funcName = funcNameBuf[0..funcNameSpan.len :0];

        var lib = std.DynLib.open(dllNameSpan) catch |err| {
            if (comptime !includeAllDebug())
                std.debug.print("{s} ({d}, {d}): couldn't open library \"{s}\"\n", .{ file, line, col, dllNameSpan });
            if (IgnoreErrorFindExternFunction)
                return;
            return err;
        };

        const func = lib.lookup(*const anyfunc, funcName) orelse {
            if (comptime !includeAllDebug())
                std.debug.print("{s} ({d}, {d}): couldn't find function \"{s}\"\n", .{ file, line, col, funcName });
            if (IgnoreErrorFindExternFunction)
                return;
            return error.FunctionNotFound;
        };

        writeTapeValue(*const anyfunc, self.tapeCursor + dllNameSpan.len + 1 + funcName.len + 1, func);
    }

    fn callExternFunction(self: *Context) !void {
        const func = externFuncMap.get(self.tape().*) orelse try self.invalidId();

        const funcPtr = readTapeValue(*anyfunc, self.tapeCursor + 1);
        const result = readTapeValue(*anyopaque, self.tapeCursor + 1 + ptrCellSize);
        var args: [256]*anyopaque = undefined;

        readTapeBytes(self.tapeCursor + 1 + 2 * ptrCellSize, @as(*ByteSpan([64]*anyopaque), @ptrCast(&args)));

        func.caller.call(funcPtr, @ptrCast(args[0..]), result);
    }

    // === threading ops

    pub fn spawnThread(self: *Context, func: anytype) !void {
        const id = self.tape().*;
        try threadMap.put(id, try ManagedThread.start(func, id));
    }

    pub fn joinThread(self: *Context) !void {
        const thread = threadMap.get(self.tape().*) orelse try self.invalidId();
        thread.join();
    }

    pub fn createMutex(self: *Context) !void {
        try MutexMap.put(self.tape().*, .{});
    }

    pub fn getMutex(self: *Context) !*Mutex {
        return MutexMap.get(self.tape().*) orelse try self.invalidId();
    }

    // === utils

    pub fn invalidId(self: *Context) !noreturn {
        std.debug.print("Invalid id {} at {}\n", .{ self.tape().*, getSignedTapePos(self.tapeCursor) });
        return error.InvalidId;
    }

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

    /// Reads into buffer until end or zero byte is reached.
    fn scanTape(comptime T: type, start: usize, buf: []T) []T {
        var len: usize = 0;
        for (0..buf.len) |i| {
            const cellVal = tapeAt((start + i) % TapeLength).*;
            if (cellVal == 0)
                return buf[0..len];
            buf[len] = @truncate(cellVal);
            len += 1;
        }
        return buf[0..len];
    }

    fn readTapeBytes(start: usize, buf: []u8) void {
        var cellIdx = start;
        var byteIdx: u32 = 0;
        while (byteIdx < buf.len) {
            const byteInCell = byteIdx % @sizeOf(cell);
            const value = tapeAt((cellIdx) % TapeLength).*;
            buf[byteIdx] = @truncate(value >> @intCast(byteInCell * 8));
            byteIdx += 1;
            if (byteIdx % @sizeOf(cell) == 0) cellIdx +%= 1;
        }
    }

    pub fn readTapeValue(comptime T: type, start: usize) T {
        var val: T = undefined;
        readTapeBytes(start, @as(*ByteSpan(T), @ptrCast(&val)));
        return val;
    }

    fn writeTapeBytes(start: usize, buf: []const u8) void {
        var cellIdx = start;
        var byteIdx: u32 = 0;
        while (byteIdx < buf.len) {
            const byteInCell = byteIdx % @sizeOf(cell);
            const value = buf[byteIdx];
            const tapeHere = tapeAt(cellIdx % TapeLength);
            if (byteInCell == 0) tapeHere.* = 0;
            tapeHere.* |= @as(cell, value) << @intCast(byteInCell * 8);
            byteIdx += 1;
            if (byteIdx % @sizeOf(cell) == 0) cellIdx +%= 1;
        }
    }

    pub fn writeTapeValue(comptime T: type, start: usize, val: T) void {
        writeTapeBytes(start, @as(*const ByteSpan(T), @ptrCast(&val)));
    }

    fn writeTapeAdd(comptime T: type, start: usize, buf: []const T) void {
        for (0..buf.len) |i| {
            tapeAt((start + i) % TapeLength).* +%= @truncate(buf[i]);
        }
    }

    fn writeTapeSub(comptime T: type, start: usize, buf: []const T) void {
        for (0..buf.len) |i| {
            tapeAt((start + i) % TapeLength).* -%= @truncate(buf[i]);
        }
    }
};

fn functionFailed(err: anyerror) noreturn {
    std.log.err("error: {s}", .{@errorName(err)});
    if (@errorReturnTrace()) |trace| {
        std.debug.dumpStackTrace(trace.*);
    }
    @panic("unhandled error");
}

inline fn discard(x: anytype) void {
    _ = x;
}

var mainArgs: []const [:0]const u8 = undefined;

var io: Io = undefined;

comptime {
    if (@sizeOf(cell) > @sizeOf(usize))
        @compileError("Cell size too large for target architecture");
}

var alloc = if (builtin.mode == .debug) std.heap.DebugAllocator(.{}).init;
const gpa = if (builtin.mode == .debug) alloc.allocator() else std.heap.smp_allocator;

pub fn main(init: std.process.Init) !void {
    defer _ = if (builtin.mode == .debug) alloc.deinit();

    io = init.io;

    mainArgs = try init.minimal.args.toSlice(gpa);

    try initMemory();
    const code = try run();
    std.process.exit(code);
}

const IgnoreErrorFindExternFunction: bool = // #f
    unreachable;
const IgnoreErrorCreateExternCaller: bool = // #c
    unreachable;

const cell = // #t
    u8;

// #g
fn run() !u8 {
    // #m
    return 0;
}

test {
    std.testing.refAllDeclsRecursive(@This());
}
