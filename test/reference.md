#### Quick Reference
* [Simple Operators](#simple-operators)
* [Macro Operators](#macro-operators)
* [Debug Operators](#debug-operators)
* [Extern Operators](#extern-operators)
* [Multithreading Operators](#multithreading-operators)
* [Table of Types](#table-of-types)

# Brainfuck Basics

A brainfuck program consists of three basic elements.
- The tape: An "infinite" array of number cells. Each cell is one byte by default.
- The cursor: The position on the tape at which operations are performed.
- The program: Instructions that are stepped through one at a time.

"infinite" is in quotes because the default size is 34 gigabytes.

# Standard Brainfuck Operators

Brainfuck as it's widely known contains 9 operators

|OP | Description|
|:-:|---
| + | Add one to the current cell
| - | Subtract one from the current cell
| < | Move cursor one cell to the left
| > | Move cursor one cell to the right
| . | Print the current cell to stdout
| , | Read one character from the stdin and write it to the current cell
|[ ]| Loop all contained operators while the current cell is non-zero

- "current cell" means the cell at the location of the cursor
- Read and write operators interpret cell values as ascii codes
- Loops test the cell before looping, meaning they may run loop zero times

\
Hello World demo from https://esolangs.org/wiki/Brainfuck
```bfpp
+++++ +++               # Set Cell #0 to 8
[
    >++++               # Add 4 to Cell #1; this will always set Cell #1 to 4
    [                   # as the cell will be cleared by the loop
        >++             # Add 4*2 to Cell #2
        >+++            # Add 4*3 to Cell #3
        >+++            # Add 4*3 to Cell #4
        >+              # Add 4 to Cell #5
        <<<<-           # Decrement the loop counter in Cell #1
    ]                   # Loop till Cell #1 is zero
    >+                  # Add 1 to Cell #2
    >+                  # Add 1 to Cell #3
    >-                  # Subtract 1 from Cell #4
    >>+                 # Add 1 to Cell #6
    [<]                 # Move back to the first zero cell you find; this will
                        # be Cell #1 which was cleared by the previous loop
    <-                  # Decrement the loop Counter in Cell #0
]                       # Loop till Cell #0 is zero

# The result of this is:
# Cell No :   0   1   2   3   4   5   6
# Contents:   0   0  72 104  88  32   8
# Pointer :   ^

>>.                     # Cell #2 has value 72 which is 'H'
>---.                   # Subtract 3 from Cell #3 to get 101 which is 'e'
+++++ ++..+++.          # Likewise for 'llo' from Cell #3
>>.                     # Cell #5 is 32 for the space
<-.                     # Subtract 1 from Cell #4 for 87 to give a 'W'
<.                      # Cell #3 was set to 'o' from the end of 'Hello'
+++.----- -.----- ---.  # Cell #3 for 'rl' and 'd'
>>+.                    # Add 1 to Cell #5 gives us an exclamation point
>++.                    # And finally a newline from Cell #6
```

# Brainfuck++

The Brainfuck++ parser is able to parse any* standard brainfuck program while also providing aditional syntax and operators to make more programs possible. \
<small>\*The Brainfuck++ parser will not ignore non-operator characters like standard brainfuck parsers.</small>

### Comments
Comments start with `#` and exclude remaining characters on the line from the program.
Comments can also be ended with `\#`.
```bfpp
# This is a comment (you saw it above)
# This comment... \# >-< # ...stopped early then
# needed another line.
```
Each comment is constrained to a single line.

### Numerable Operators
Instead of repeating operators like `+` many times, a postfix number can be added.

Works with `+`, `-`, `<`, `>`
```bfpp
>12 +72 . <12
```
### Modifying by Characters and Strings
Instead of looking up keycodes adding each character in a row, `+` and `-` operators can work with characters and strings
```bfpp
+'w' # Adds ascii code for 'w'
-'w' # Same for subtracting
+"Hello\nWorld!" # Adds each character to it's own cell without moving the cursor
[.>] # prints "Hello World!" with a newline in the middle.
```
Supported Escape Characters
|Escape| Character|
|:-:|---
|\\\\ | Backslash
|\\"| Double Quote
|\n| Newline
|\b| Backspace
|\t| Tab

## Brainfuck-Like Features
Brainfuck++ adds several simple operators, similar to those of standard brainfuck.
### For loops
Groups of operators can be repeated several times by using for loops. For loops are a matching pair of curly brackets, with the openning bracket optionally postfixed with a number.
```bfpp
# Repeats 3 times
{3
    +33
    .
}
# Prints "!Bc"

```
Like the numerable operators, omitting the number postfix defaults to executing the contents once.

### Delays
Some programs need to be able to wait for a duration.
The delay operators waits until a specified amount of time has passed since the last time a delay operator was executed (or program start if it's the first time). Delays are specified as a postfixed number of milliseconds. No prefix means no delay, which just resets the "last executed time" to now.
```bfpp
!50 # Wait for 50 milliseconds after program start

!!100 # Waits for an absolute 100 millseconds
# (First wait immediately resets timer and second wait pauses)

+ [ # This loop regulates itself to 50 ms per cycle regardless
    # of the time taken executing the body.
    !50
    ... # Code that takes time
]
```
### Referencing
For some purposes it's important to be able to reference and dereference cells.
```bfpp
# An asterisk writes the memory address of the cursor the
# next eight cells. This means starting from the cursor and
# moving right.
*
# Cells 0-7 were written to

# A tilde interprets the next eight cells as a memory address,
# reads a byte at that address, then writes it to the tape
# right after the address.
~
# Cells 0-7 were read and cell 8 was written to

```

### Simple Operators
|OP |Description| Default *n*
|:-:|---|---
|{*n* }| Repeats it's contents n times| 1
|!*n*| Waits n until milliseconds since the last wait| 0
|*| Writes the address of the cursor to the tape
|~| Reads a byte at the address of the tape

## Macros
Macros fix the problem of writing the same code over and over again.
Macros are defined anywhere in code and can be invoked in any sub-scope. A scope is any pair of open/closing brackets of any type. Macro names are restricted to numbers, letters, and underscores and cannot start with a number.
```bfpp
# Ampersand to define
&foo(
    ...
)

# Dollar sign to invoke
+60 $foo

[
    $foo # Macros can be found by looking in enclosing scopes,

    # and can be defined and used in any scope.
    &bar(
        -3 .
    )

    $bar
]

# Macros can't be found by looking in child scopes.
$bar # COMPILE ERROR: Macro "bar" cannot be found
```

Macro definitions have the ability to be exporting, meaning they are visible from outside of a loose scope. Macro export operators are another way of doing this, finding any visible macro and relaying it out of a loose scope. Loose scopes include while loops, for loops, and macro invocations.

```bfpp
&foo(
    # Bar isn't immediately visible outside of foo
    # because macro definitions are not loose scopes.
    &&bar(
        ...
    )
    ...
)

# One-time for loops are handly for scoping
{
    $foo

    # Invoking foo copies it's contents into a local
    # invocation scope, including the definition of bar.
    # Since a macro invocation is a loose scope,
    # bar gets exported to this parent scope, making it
    # visible for use.
    $bar

    # Using &$ an existing visible macro can be exported
    # out of the current loose scope.
    &$bar
}

# Because of the relaying export in the for loop, bar
# becomes visible here.
$bar
```

Because this wasn't complicated enough yet, macros also support arguments. Macro arguments are similar to macro bodies in that they are a group of operators that can be invoked.
```bfpp
# Macro parameter requirements aren't
# a part of the definition.
&do_things_elsewhere (
    >64
    # Macro args are macros themselves with numbers for names
    $0
    >64
    $1
    <128
)

# macro args are a list of bracket pairs with contained code
$do_things_elsewhere (...) (+++)
                    # $0    $1
```
Macro argument counts are validated when a macro is invoked and it tries to invoke it's parameters. E.g. if a macro is invoked with fewer than three arguments and the macro contains `$2`, a compile error will be raised. This means that macros can be given as many extra aguments as you would like and they will just be ignored.

Since macro args are in all ways macros themselves, they also support args. This is where things can get confusing.
```bfpp
# This macro expects the first arg to be a "repeater".
# A repeater is a pattern where an argument is passed
# which calls *it's* argument a fixed number of times.
&clear_n_cells (
    # repeat n times: clear this cell and advance 1
    $0 ([-]>)
    # repeat n times: retreat 1
    $0 (<)
)

# This will clear the next 5 cells and return
# to where it started.
$clear_n_cells (
    {5  # The $0 here refers to the argument passed
        # to this argument we're inside of.
        $0
    }
)

# And this will clear the next 12
$clear_n_cells ({12 $0 })
```
Multiple macros with the same name cannot be created in the same scope, but names can be overridden in child scopes. Scope backtracking can be used to differentiate between these identical names. Scope backtracking can also be used with arguments to invoke the arguments of parent functions.
```bfpp
&foo (+++)

{
    # overriding foo is allowed
    &foo (---)

    # calls foo from the closest scope
    $foo # subtracts three

    # backtracks one scope then finds foo
    $$foo # adds three
}


# expects two args
&boba (
    &&kiki (
        $0 # invokes first arg passed to kiki
        $$0 # invokes forst arg passed to boba
    )
)

$boba (...) # exports a copy of kiki which has access to "..."

$kiki (+64) # adds 64 then prints three times
```
When used carefully this can be used for purposes like "forwarding" repeater arguments.
```bfpp
# $0 = repeater
&clear (
    $0([-]>)
    $0(<)
)

# $0 = repeater
&foo (
    # Backtracks out of clear's first arg to foo's scope.
    #      |  Backtracks out of the first "$$0"'s first arg
    #      |  to the first arg to clear's scope.
    #      V   V
    $clear($$0($$0))
    $0(+>)
    $0(<)
    $clear($$0($$0))
)

$foo ({4 $0 }) # clears four cells, writes 1 to four cells, then clears them again

```
Backtracking can also works for export operators and can be repeated to backtrack multiple levels instead of just one by adding more dollar signs. (`$$$$foo` can be valid)

One extra note is that macros defined directly are visible before there definitions, but mactros exported through invocations are only visible after the relevant function call.
```bfpp
$bar # COMPILE ERROR: Macro "bar" cannot be found

$foo # Usage before definition is fine when foo is defined directly

$bar # bar can be found after it's exported from foo

{
    # Even though foo is being exported, it's not through an invocation
    # so it can be found just fine.
    &&foo(
        &&bar (...)
    )
}
```

### Built-in Macros
Built-in macros work a little differently to normal macros. Invoking a builtin-macro requires exacly one parameter which must be a string.
|Builtin|Description
|---|---
|import|Invokes the contents of another bfpp source file
|embed_file|Embeds a file into the binary, writing it's memory address to the tape

Imports work exactly like if the entire contents of the imported file is one big macro being invoked. 

```bfpp
# === utils.bfpp ===
+"Utils Loaded"
[.>]<[[-]<]>

&&clear (
    $0([-]>)
    $0(<)
)
```
```bfpp
# === main.mfpp ===
$import("utils.bfpp") # Prints "Utils Loaded" immediately and exports clear
$clear({5$0})


$embed_file("data.txt") # Writes address of data to the next eight bytes on tape

~ >8 . # prints the first character in data
```
File paths are interpreted as relative to the source file they are written in, or may be absolute paths. File embeds are deduplicated automatically.

### Macro Operators
|OP |Backtracking|Description
|---|---|---
|&name( )| | Defines a macro
|&&name( )| | Defines and exports a macro
|$name|$$name|Invokes a macro with matching name
|$*n*|$$*n*|Invokes an argument within a macro
|&$name|&$$name|Finds and exports an existing macro

## Debugging

Several debug operators have been added for the sake of mainining sanity.
All debug characters print information about where they were written in the source code.

```bfpp
# debug assert
@12 # asserts that the cursor position is cell 12, crashing otherwise
# print looks like:
# demo.bfpp (2, 1): assert failed! (0 != 12)

@-2 # Works with negtive positions

# debug print
@. # prints the current cell position and value
# print looks like:
# demo.bfpp (7, 1): value at 0 is 0

# debug print dump
@.8 # prints the positions and values of multiple consecutive cells
# print looks like:
# demo.bfpp (12, 1): memory tape dump from 0 to 7
# |+0 |+1 |+2 |+3 |+4 |+5 |+6 |+7 |
# |0  |0  |0  |0  |0  |0  |0  |0  |

# debug quit
@! # stops the program
```
All debug operators can optionally be postfixed by a string to print a message when they execute.
```bfpp
# Only prints the message when assert fails
@0"Cursor isn't at 0"

# Prints the message after the value
@.2"Value of ipsum lorem"

# Can also just print and no nothing else
@"This code just ran"
```
Debug asserts also have a variant for macros where they test relative to where the macro started
```bfpp
&foo (
    < +
    @$-1 # asserts that cursor is -1 from where the macro started
    >
    @$ # no number defaults to zero
)
```
### Disabling Debug
Debug operators are disabled by certain options in the build process.
When emitting bf, include_debug_operators filters all debug operators.
When building zig, different release modes filter different operators.
|Zig Mode|Result
|---|---
|Debug| All debug operators
|ReleaseSafe| Only assert and quit
|ReleaseFast| No debug operators

### Debug Operators
|OP |Description|*n*
|:-:|---|---
|@*n*| Assert cursor is at *n*|mandatory
|@$*n*| Assert cursor is offset *n* from start|default 0
|@.*n*| Print cell locations and values|default 1
|@!| Quit program
|@""| Print

## Extern Functions
In order for bfpp to interoperate with the rest of the computer, it needs to be able to call functions in other binaries. This is what Extern Operators are for.\
To call an extern function, it takes three steps.
Each operator reads a pattern of bytes from the tape to accomplish it's job.

#### Step 1: Find the function ptr
The Find Extern operator scans the tape for two null terminated strings then writes an 8 byte pointer after those strings.
Memory layout relative to the cursor:
```
|0 |1 |...|n |n+1|n+2|...|m |m+1|m+2| ... |m+9|
|Lib name    |0  |Func name |0  |Func Address |
|Inputs                         |Output       |
```

```bfpp
+"glfw3"
[>]>
+"glfwCreateWindow"
<<[<]> @0
%* # find the address of the function and write it to the tape
[>]>[>]>
@.8 # print the address
```

#### Step 2: Create the caller
The Create Caller operator scans the tape for a null-terminated sequence of type ids and writes a caller id to the tape. The number of callers that can be created is limited by the cell size. Each id returned is sequential starting from zero.\
[Table of Types](#table-of-types) \
Arguments are optional, but a return type is required. Type void is not permitted as an argument type.
Memory layout relative to the cursor:
```
With args
|0       |1 |...|n |n+1|n+2   |
|Ret type|Arg Types|0  |Id    |
|Inputs                |Output|

Without args
|0       |1 |n+1   |
|Ret type|0 |Id    |
|Input      |Output|
```
```bfpp
# set types
#ptr  int   int   ptr   ptr   ptr
+12 > +14 > +14 > +12 > +12 > +12
<5
%& # create the caller
[>]> @. # Prints the caller id
```
Creating a caller if all caller ids are already taken will stop the program with an error.

#### Step 3: Call the function
The Extern Call operator reads a caller id, function pointer, return address and argument addresses from the tape. This operator doesn't directly write to the tape, but if the function returns non-void, then the provided return address will be written to.\
Memory layout relative to the cursor:
```
|0        |1 |...|8 |9 |...|18 |19 |...|n |
|Caller id|Func Addr|Ret Addr  |Arg Addrs |
```
`*` is usefull for getting addresses of cells on the tape to use as the return address and arg addresses. Be warned, the return and arg addresses need to be aligned for the types they are. \
Example without args because it's shorter to show (with glfwInit)
```bfpp
&move_8_from_to(...) # moves 8 cells from one offset to another
&glfwInitAddr(...) # writes 8 cell function ptr
&callerId(...) # writes 1 cell caller id

$callerId # write caller id to cell 0
> &glfwInitAddr # write function pointer to cells 1-8
>31 * <32 # take a reference to cell 32
@0 &move_8_from_to ({32$0}) ({9$0}) # move address of cell 32 to cells 9-18

%$ # call the function
>32 # go to where the return was written
@. # print result (0 or 1 for glfwInit)
```

### Extern Operators
|OP |Description
|:-:|---
|%*| Finds the address of an external function
|%&| Creates a caller for a certain function signature
|%$| Calls a function using a function caller

## Functions
While calling external functions is very useful on it's own, sometimes internal functions are need for callbacks or mutlithreading. The syntax for defining functions is reminicent of macros, but with some added information. Function definitions use the same type ids as the Create Caller operator. \
[Table of Types](#table-of-types)

```bfpp
# Defines a function that returns void. 
&*foo~1 (
    ...
)

# Defines and exports a function that returns a pointer and
# takes two i32s as args.
&&*bar~12~7,7 (
    ...
)

# Looks like a call, but writes the address of
# foo to the next eight cells.
$*foo

# Supports scope backtracking
$$*bar

# As functions and macro can't share names, the name export operator works on functions
&$foo
```
Functions cannot be defined within other functions. There is no in-built way to directly call a function with an arbitary signature, so the extern call operators must be used (`%&` and `%$`).

There are function operators for dealing with function args and return values as well.
```bfpp
# This function will return it's argument plus one
&&*foo~2~2 (
    # Writes the memory address of an argument to the tape
    $*0
    # While it uses the same operator as getting a function
    # pointer, it does not support scope backtracking.

    ~ >8 +

    # Returns the value on the tape, reading
    # as many cells as needed for the type.
    $!
)
```
Getting argument addresses and retuning both work outside of function as well.
Using a Get Func Arg operator outside of a function (lexically) accesses `argc: usize` and `argv: **u8`.
Using a Func Return operator outside of a function (scope-wise) ends the program.
```bfpp
&foo (
    $*0 # accesses argc because it's not *written* in a function

    $! # returns out of bar because it's currently running
)

&&*bar~2~2 (
    $foo
)

$! # returns out of the program
>+ # COMPILE ERROR: Unreachable code (blame zig)
```
### Function Operators
|OP |Backtracking|Description
|---|---|---
|`&\*name~*r*( )| | Defines a function with no args
|&\*name~*r*~*a*,*a*( )| | Defines a function with args
|&&\*name~*r*( )| | Defines and exports a function with no args
|&&\*name~*r*~*a*,*a*( )| | Defines and exports a function with args
|$*name|$$*name| Gets the address of a function
|$*n*|| Gets the address of a function argument
|$!|| Returns from a function with the value at the tape cursor
|&$name|&$$name|Finds and exports an existing function

## Multithreading
Multithreading exists as a language feature of Brainfuck++ because why not at this point? \
Functions defined with the signature `void(cell)` can be called by the Threadded Call operator.
There is no limit to how many threads can be created, but there is a limit to how many can be kept track of at once. Each time a thread is created, an id will be written to the current cell. Old ids will eventually be reused even if the old threads of those ids are still running.
```bfpp
&&*foo~1~20 (
    @"Hello from another thread!"
)

# Starts a thread which runs foo and writes the
# thread id to the current cell.
^$foo

# Reads a thread id from the current cell and
# waits until that thread ends.
^!
# If that thread already ended or was never
# started then it does nothing.
```
To make multithreading safer, mutexes can be used to coordinate data access.
```bfpp
^&mut1 # This defines a mutex.

^$foo
>
# This creates a locking mutex block.
# Only one thread at a time can enter
# a mutex block of any given mutex.
^{mut1
    ++
}

< ^!

&*foo (
    >
    # If any thread enters any block with this mutex, then no other thread
    # may enter any block with this mutex until the first thread exits the block.
    ^{mut1
        --
    }
    # While in this example both threads try to enter a mutex block of the
    # mutex "mut1" at the same time, only one is allowed in at a time.
    # A consistent order is not guaranteed.
)
```
Backtracking and exporting is allowed for all relevant operators
```bfpp
&*foo~1~20(...)

^&&mutex
&$mutex
[
    ^$$foo

    # Backtracking is a little weird with this one.
    ^{$mutex

    }
]
```

### Multithreading Operators
|OP |Backtracking|Description
|---|---|---
|^$name|^$$name| Spawn thread for a function
|^!|| Join a thread (waits for completion)
|^&name|| Defines a mutex
|^&&name|| Defines and exports a mutex
|^{name }|^{$name }| Creates a locking mutex block
|&$name|&$$name| Finds and exports an existing mutex

## Table of Types
|Id|Type
|:-:|---
|1|void
|2|u8
|3|i8
|4|u16
|5|i16
|6|u32
|7|i32
|8|u64
|9|i64
|10|f32
|11|f64
|12|pointer
|13|c uint
|14|c int
|15|c ulong
|16|c long
|17|c long double
|18|size
|19|usize
|20|cell

Cell is an unsigned integer the size of a cell on the memory tape.

```bfpp
+"hello world"
[.>]
```