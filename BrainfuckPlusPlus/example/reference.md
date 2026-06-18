#### Quick Reference
* [Simple Operators](#simple-operators)
* [Macro Operators](#macro-operators)
* [Debug Operators](#debug-operators)
* [Extern Operators](#extern-operators)
* [Multithreading Operators](#multithreading-operators)
* [Table of Types](#table-of-types)
* [Project Configuration](#project-configuration)

# Brainfuck Basics

A Brainfuck program consists of three basic elements.
- The tape: An "infinite" array of number cells. Each cell is one byte by default.
- The cursor: The position on the tape at which operations are performed.
- The program: Instructions that are stepped through one at a time.

"infinite" is in quotes because the default size is only 34 gigabytes.

# Standard Brainfuck Operators

Brainfuck as it's widely known contains 9 operators:

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
- Read and write operators interpret cell values as ASCII codes
- Loops test the cell before looping, meaning they may run zero times

\
A simple demo to echo text back to the terminal.
```bfpp
+ # set a looping flag to 1
[ # while this cell is nonzero
    -       # set the looping flag to zero
    >>      # move to cell 2
    [>]     # move to the end of the sequence characters
    >>+<<   # set a barrier two cells after the equence of characterss
    ,       # read input from the terminal to the end of the sequence
    -'\n'   # subtract 10 (ascii for line feed) from the input
    [       # if the input is non-zero (if input != newline)
        [<]<    # move back to the looping flag
        +       # restore the looping flag to one
        >>[>]   # move to empty cell after the squence
        # The tape and cursor are now in one of two states:
        #
        # If the character was a newline:
        #   loop flag      input - \n = 0, cursor is here
        #    | always zero |  always zero and one
        #    v   v         v   v   v
        #   |0  |0  | ... |0  |0  |1  |
        #              ^
        #   multiple cells with
        #   sequence of inputted characters
        #
        # If the character was not a newline:
        #                  input - \n != 0
        #                  |  cursor is here
        #                  v   v   
        #   |1  |0  | ... |x  |0  |1  |
        #
        # In both cases, the cursor ends on an empty cell,
        #  so this loop is only really an if statement
    ]
    # Now the cursor needs to be realigned to the same position
    
    > # moves the cursor to either the zero after the input cell or the barrier set to one
    [<] # in the case that the cursor is on the barrier,
        #   it moves left to align with the other case
    >- # clear the barrier cell
    < +'\n' # add 10 back to to input to restore it
    [<]< # return back to the loop flag to setup for the next loop
] # if the last inputted character was a newline, the loop flag was cleared and the loop will end here
>> # move to the start of the character squence
[.>] # print and move along each character
```

# Brainfuck++

The Brainfuck++ parser is able to parse any* standard Brainfuck program while also providing aditional syntax and operators to make more programs possible. \
<small>\*The Brainfuck++ parser will not ignore non-operator characters like standard Brainfuck parsers.</small>

### Comments
Comments exclude any characters from the code being executed.
They are started with a hash `#` and span until the end of the line or a backslash+hash `\#`.
```bfpp
> # This is a comment (you saw it above)
+ # This comment... \# >-< # ...stopped early then
< #  needed another line.
```

### Numerable Operators
Instead of repeating operators like `+` many times, a postfix number can be added.

Works with `+`, `-`, `<`, and `>`.
```bfpp
>12 +72 <24
```
### Modifying by Characters and Strings
Instead of looking up keycodes, the characters can be written directly in single quotes `' '` after `+` and `-` operators. Using double quotes `" "` allows entire strings to be added or subtracted from the tape at once. Special characters can be written with a backslash `\` followed by an escape code (see table below).
```bfpp
+'w' # Adds ascii code for 'w' (same as +119)
-'w' # Same for subtracting
+"Hello\nWorld!" # Adds each character to its own cell without moving the cursor
[.>] # prints "Hello World!" with a newline in the middle.
```
Supported Escape Codes:
|Code| Character|
|:-:|---
|\\\\ | Backslash
|\\"| Double Quote
|\n| Newline
|\b| Backspace
|\t| Tab
|\0| Null (zero byte)

## Brainfuck-Like Features
Brainfuck++ adds several simple operators, similar to those of standard Brainfuck. The goal of these is to eliminate repetition and add important capabilities.

### For loops
Groups of operators can be repeated several times by using for loops. They are defined with a matching pair of curly brackets `{ }`, with an optional number after the first bracket to define how many times it should repeat.
```bfpp
# Repeats 3 times
{3
    +33
    .
}
# Prints "!Bc"

```
Like the numerable operators, omitting the number defaults one, executing the contents once. This can be useful for creating scopes (see [Macros](#macros)).

### Delays
Some programs need to be able to wait for a duration or maintain a constant framerate.
The delay operator enables this by waiting until a specified amount of time has passed since the last time a delay operator was executed. Delays are defined as an exclaimation mark `!` optionally followed by a number of milliseconds. Unlike numberable operators, omitting the number defaults to zero milliseconds, in other words this immediately resets the saved last time to now. When the program starts, it initializes the saved last time to now.
```bfpp
... # Code that takes time

!50 # Wait until 50 milliseconds have passed since program start
    # (regardless of how long the above code takes to complete)

... # More code that takes time

!!100 # Waits for an absolute 100 millseconds from now
# (Two separate waits; first immediately resets timer, and second pauses)

+ [ # This loop regulates itself to 50 ms per cycle regardless
    #  of the time taken executing the body.
    !50
    ... # Code that takes time
]
```
### Referencing
When interacting with outside code it's important to be able to reference cells and dereference pointers. The reference operator is an asterisk `*` and it writes the address of the cursor to the next 8 cells. The dereference operator is a tilde `~` and it reads a pointer from the next 8 cells and writes the byte at that memory address to the 9th cell. Next 'n' cells refers to cells starting at the cursor location and moving right.
```bfpp
* # writes to cells 0-7 with the address of cell 0

>16

~ # reads a pointer from cells 16-23 and
  #  writes the value at that memory address to cell 24
```

### Input Mode
By default, input from the console isn't readable until enter is pressed, and any input the user gives is visibly written. In the case that this isn't desireable, the **Input Mode** operator is the solution. Defined by an exclaimation mark and a comma `!,`, it reads the current cell for the input mode. If the cell is zero, it switches to raw mode where the input is not echoed and unbuffered. If the cell is non-zero, it switches to buffered mode (the default).

### Simple Operators
|OP |Description| Default *n*
|:-:|---|---
|{*n* }| Repeats its contents n times| 1
|!*n*| Waits until n milliseconds since the last wait| 0
|*| Writes the address of the cursor to the tape
|~| Reads a byte at the address on the tape
|!,| Switches input mode

## Macros
Macros fix the problem of writing the same code over and over again.
Macros are defined with the **Macro Define** operator: an amersand `&` followed by the **name** of the macro being defined, then a set of brackets `( )` which contain the code what will be pasted in at an **invocation site**. Macros can be **invoked** with the **Macro Invoke**: a dollar sign `$` followed by the **name** of the macro being invoked. There's also more to this operator that will be covered below.
 
Unlike functions in other languages, macros aren't "called" in the way that the program jumps into and out of them during execution. Macros are **invoked** at compile time, copying the body of the definition and pasting at the **invocation site**. This is similar to inline functions.

Macros exist as **names** within **scopes**. **Scopes** are regions of code bounded by a pair of brackets of any kind. Scopes have **inner scopes** (scopes within them) and **outer scopes** (scopes that enclose them).
**Names** are a system of identification used by more than just macros.

```bfpp
# Defines macro "foo"
&foo(
    ...
)

+60
# Invokes macro "foo"
$foo
# This pastes the body of foo (three prints) into the code right here

[
    $foo # Macros from outer scopes can be used in inner ones.

    # Macros can be defined in any scope, not just the top level of the file
    # but it is only visible here or in an inner scope.
    &bar(
        -3 .
    )
    $bar # same scope, so it's visible
]

# Macros can't be used outside the scope in which they are defined.
# (at least not without extra effort)
$bar # COMPILE ERROR: Name "bar" cannot be found
```
**Invocation sites** are also scopes. This means that macros cannot be seen outside of them.

```bfpp
&foo(
    # bar is only visible within foo
    &bar(
        ...
    )
)

# While invoking foo pastes a copy of the bar definition into the code here,
# it's inside of the invocation site body, meaning it's not visible.
$foo

$bar # COMPILE ERROR: Name "bar" cannot be found
```
### Exporting
**Exporting** is the process of making a **name** visible to the immediate **outer scope** if it's within a **loose scope**.

**Loose scopes** are a special subset of scopes which include while loops, for loops, and macro invocations.

Macro definitions can be made **exporting** by using two ampersands `&&`. The **Name Export** defined as an ampersand and a dollar sign `&$` followed by a **name** operator is another way of **exporting**. It exports any already existing visible macro.

```bfpp
{5
    [
        # This would normally not be visible to the outer scope,
        # but because the Macro Define is exporting and while loops
        # are a loose scope, it becomes visible to the outer scope.
        &&foo(
            ...
        )
    ]
    $foo # so this works

    # Since exporting a name only goes out by one scope,
    # an additional export is required to make foo visible
    # outside of the for loop.
    # The Name Export operator is perfect for when a name
    # needs to be "relayed" out.
    &$foo
}
```

```bfpp
&foo(
    # Bar isn't visible outside of foo because
    # macro definitions are not loose scope.
    &&bar(
        ...
    )
    ...
)

# One-time for loops are handy for making scopes
# without affecting program execution.
{
    # Invoking foo creates a copy of the definition of bar.
    # Since that definition is exporting and invocation sites
    # are loose scopes, bar gets exported into this scope.
    $foo

    # so this works
    $bar
}

```
### Parameters / Arguments
Because macros weren't ~~complicated~~ capable enough already, they also support parameters. Macro parameters are similar to macro definitions in that they define a group of operators that can be **invoked**. 

Arguments are given by a **Macro Invoke** operator by adding several bodies after it, each a pair of brackets `( )` enclosing code. Within a the body of a **Macro Define**, arguments can be invoked like macros with numbers for names, starting from zero. Listing required parameters isn't part of the **Macro Define** operator, instead it's based on the highest-index parameter used in the body and extra arguments are allowed.

```bfpp
&do_things_elsewhere (
    >64
    # Invokes the first argument, pasting "..."
    $0
    >64
    # Same for the second arg 
    $1
    <128
)

$do_things_elsewhere (...) (+++)
                    # $0    $1
```
Since macro args are in all ways macros themselves, they support parameters of their own. This is where things can get confusing.

**A use case for args with args** \
Since macro arguments need to consist of complete operators, numbers like "how many times to repeat an add" can't be passed simply. This is why I created a pattern I call "repeaters", which are arguments consisting of a for loop which run *their*
first argument a specific number of times.
```bfpp
# This macro expects the first arg to be a "repeater".
&clear_n_cells (
    # repeat n times: clear this cell and advance 1
    $0 ([-]>)
    # repeat n times: retreat 1
    $0 (<)
)

# This will clear the next 5 cells and return
# the cursor to where it started.
$clear_n_cells (
    {5 # this 5 is the 'n' in the comments above, decided by the invoker.
        # The $0 here refers to the argument passed
        # to this argument we're inside of.
        $0
    }
)

# And this will clear the next 12
$clear_n_cells ({12 $0 })
```
### Backtracking
Multiple macros with the same name cannot be created in the same scope, but names can be overridden in child scopes. Scope **backtracking** can be used to differentiate between these identical names. **Backtracking** is the process of starting the search for a name in an outerscope instead of the current one. This skips over locally defined macros with names that override macros in outer scopes. This isn't terribly useful with regular macros, but it's essential in differentiating parameters across multiple visible scopes, all with their own parameters.

```bfpp
&foo (+++)

{
    # This overrides the name "foo"
    &foo (---)

    # Calls foo from the closest scope
    $foo # subtracts three

    # Backtracks one scope then finds foo
    $$foo # adds three
}



&boba (
    # Backtracking lets kiki have
    # access to the parameters of boba.
    &&kiki (
        $0 # Invokes first arg passed to kiki
        $$0 # Invokes first arg passed to boba
    )
)

$boba (...) # Exports a copy of kiki which has access to "..."

$kiki (+64) # Adds 64 then prints three times
```
When used carefully this can be used for advanced tasks like "forwarding" repeater arguments.

**Backtracking** always works lexically (in text) which means if you see a backtrack, all you have to do is physically look one or more sets of brackets out and then think about which macro or parameter the invocation refers to.
```bfpp
# $0 = repeater
&clear (
    $0([-]>)
    $0(<)
)

# $0 = repeater
&foo (
    # Backtracks out of clear's first arg to the body of foo,
    #      | where it gets the first arg of foo.
    #      |   Backtracks out of the first "$$0"'s first arg
    #      |   to the body of clear's first arg.
    #      V   V
    $clear($$0($$0))
    $0(+>) # Invokes the repeater directly like before
    $0(<)
    $clear($$0($$0))
)

# Clears four cells, writes 1 into four cells, then clears them right after.
$foo ({4 $0 })
```
Backtracking can also works for export operators and can be repeated to backtrack multiple levels instead of just one by adding more dollar signs. (`$$$$foo` can be valid)

One oddity with how macros are implented is that macros defined directly are visible before their definitions, but macros exported through invocations are only visible only after the relevant function call.
```bfpp
# bar hasn't been exported yet
$bar # COMPILE ERROR: Macro "bar" cannot be found

$foo # Usage before definition is fine when foo is defined directly

$bar # bar is visible after it's exported from foo

{
    # Even though foo is being exported, it's not through an invocation
    # so it can be found just fine.
    &&foo(
        &&bar (...)
    )
}
```

### Built-in Macros
Built-in macros are like regular macros, except they accomplish more complicated tasks related to project and file management. Invoking a builtin-macro requires exacly one parameter which must be a string. Built-in macros cannot have their names overridden in any scope.

|Builtin|Description
|---|---
|import|Invokes the contents of another bfpp source file, treating like one big macro being invoked.
|embed_file|Embeds a file into the output binary, writing the memory address of the file's data to the tape.

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
# === main.bfpp ===
$import("utils.bfpp") # Prints "Utils Loaded" immediately and exports the clear macro
$clear({5$0})

$embed_file("data.txt") # Writes address of data to the next eight bytes on tape

~ >8 . # prints the first character in data.txt
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

Several debug operators have been added for the sake of maintaining sanity.
All debug operators print information about where they were written in the source code and start with an at sign `@`.

#### Debug Assert
An at sign followed by a number (can be negative) `@n  @-n` \
Asserts that the cursor position is at a specified position. Stops the program if the check fails.
```bfpp
@12 # Assert at cell twelve
# Print looks like:
# demo.bfpp (2, 1): assert failed! (0 != 12)

@-2 # Assert at cell negative two
```

#### Debug Print
An at sign and a period `@.` \
Prints the current cursor position and cell value.
```bfpp
@.
# Print looks like:
# demo.bfpp (7, 1): value at 0 is 0
```
#### Debug Print Dump
An at sign, a period and a number `@.n` \
Prints the positions and values of multiple consecutive cells.
```bfpp
@.8 # Print next eight cells
# Print looks like:
# demo.bfpp (12, 1): memory tape dump from 0 to 7
# |+0 |+1 |+2 |+3 |+4 |+5 |+6 |+7 |
# |0  |0  |0  |0  |0  |0  |0  |0  |
```
#### Debug Quit
An at sign and an exclaimation mark `@!` \
Simply stops the program
```bfpp
@!
```
#### Debug Relative Assert
Debug asserts also have a variant for macros where check test relative to where the macro started. They (sadly) do not support backtracking.
```bfpp
&foo (
    < +
    @$-1 # asserts that cursor is -1 from where
         # it was when the macro started.
    >
    @$ # asserts that cursor is where it was
       # when the macro started.
)
```
### Messages
All debug operators can optionally be followed by a string to add a message to their regular outputs.
```bfpp
# Only prints the message when assert fails
@0"Cursor isn't at 0"

# Prints the message after the value
@.2"Value of lorem ipsum"

# Can also just print and no nothing else
@"This code just ran"
```

### Disabling Debug
Debug operators are disabled by certain options in the project file. \
When emitting bf, debug operators can be exluded from the output by adding this to the project toml config.
```toml
[bf]
include_debug_operators=false
```
When building zig, different release modes filter different debug operators.
```toml
[zig]
args=["-Doptimize=<mode>"]
```
|Zig Mode|Result
|---|---
|Debug| All debug operators
|ReleaseSafe| Only assert and quit
|ReleaseFast| No debug operators
|ReleaseSmall| No debug operators

### Debug Operators
|OP |Description|*n*
|:-:|---|---
|@*n*| Assert cursor is at *n*|mandatory
|@$*n*| Assert cursor is offset *n* from start|default 0
|@.| Print cursor and cell
|@.*n*| Print cell locations and values|default 1
|@!| Quit program
|@""| Print

## IDs
Before the last few sections, it's important to specify what an **ID** is. **IDs** are numbers that represent a value at runtime, unlike **names** which represent something at compile time. **IDs** are like slots and over the course of a program's execution certain types of values can be written to and read from those slots. There are as many ID slots as can be represented by one cell. Re-writing to the same **ID** deletes the preview value and reading from an unwritten **ID** stops the program. Each type of storable value has it's own ID space, so the same **ID** can represent multiple different values of different types.

## Extern Functions
In order for bfpp programs to interoperate with the rest of the computer, it needs to be able to call functions in other binaries. This is what Extern Operators are for.\
To call an extern function, it takes three steps.
Each operator reads a pattern of bytes from the tape to accomplish its job. All extern operators start with a percent sign `%`.

#### Step 1: Find the function ptr
The **Find Extern Func** operator is a percent sign and a asterisk `%*`.
It scans the tape for two null terminated strings then writes an 8 byte pointer after those strings.
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
The **Create Extern Caller** operator is a percent sign and a ampersand `%&`. Extern callers are stores with **IDs** (see above). This operator reads an **ID** from the tape and scans for a null-terminated sequence of type indexes. For reference on index to type mapping, see the [Table of Types](#table-of-types).
Arguments are optional, but a return type is required. Type void is not permitted as an argument type.
Memory layout relative to the cursor:
```
|0  |1       |2 |...|n |
|ID |Ret type|Arg Types|
              ^ may be zero cells long
```
```bfpp
+1 # set id
# set types
# ptr   int   int   ptr   ptr   ptr
> +12 > +14 > +14 > +12 > +12 > +12
<6
%& # create the caller
```

#### Step 3: Call the function
The **Call Extern Function** operator reads a caller **ID**, function pointer, return address and argument addresses from the tape. This operator doesn't directly write to the tape, but if the function returns non-void, then the returned value will be written to the provided return address.\
Memory layout relative to the cursor:
```
|0        |1 |...|8 |9 |...|18 |19 |...|n |
|Caller ID|Func Addr|Ret Addr  |Arg Addrs |
```
`*` is usefull for getting addresses of cells on the tape to use as the return address and arg addresses. Be warned, the return and arg addresses need to be aligned for the types they are. \
Example without args because it's shorter to show (with `glfwInit`)
```bfpp
&move_8_from_to(...) # moves 8 cells from one offset to another
&glfwInit_addr(...) # writes 8 cell function ptr
&caller_id(...) # writes 1 cell caller id

$caller_id # write caller id to cell 0
> &glfwInit_addr # write function pointer to cells 1-8
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
While calling external functions is very useful on its own, sometimes custom functions are needed for callbacks or multithreading.

Code inside of a function uses a different **Context** from the main program, meaning it has an independant cursor position and last time for wait operators. It still shares the same memory tape and **ID** values.

The **Define Function** operator is an ampersand and asterisk follwed by a **name**, tilde, and return type id `&*name~n`.
Optionally after that can come another tilde then a comma-separated list of type indexes for parameter `&*name~n~n,n`. Parameters or no, a set of brackets comes last to define the function body.

Functions use the same systems of **names**, **scopes**, **exporting**, and **backtracking** as macros.

```bfpp
# Defines a function that returns void. 
&*foo~1 (
    ...
)

# Defines and exports a function that returns 
# a pointer and takes two i32s as args.
&&*bar~12~7,7 (
    ...
)
```
Functions can be accessed by their address.
The **Get Function** operator is a dollar sign and an asterisk `$*`. It gets the address of a function defined in the project and writes it to the next eight cells.
```bfpp
# Looks like a call, but writes the address of
# foo to the next eight cells.
$*foo

# Get Function supports scope backtracking
$$*bar

# As functions and macros can't share names,
# the same Export Name operator works.
&$foo
```
Functions cannot be defined within other functions. There is no in-built way to directly call a function with an arbitary signature, so the extern call operators must be used (`%&` and `%$`).

### Associated Operators

There are operators for dealing with function args and return values.
The **Get Function Param** operator is a dollar sign, asterisk, and number `$*n`. This operator writes the memory address of an argument to the next eight cells of the tape.

The **Function Return** operator immediately ends execution of the current function and returns the current value on the tape, reading as many cells as needed for the type.

```bfpp
# This function will return its argument plus one
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
Getting argument addresses and returning both work outside of function as well.
Using the **Get Function Param** operator outside of a function (lexically) accesses `argc: usize` and `argv: **u8`.
Using the **Function Return** operator outside of a function (call-wise) ends the program and returns an exit code.
```bfpp
&foo (
    $*0 # Accesses argc because it's not *written* in a function

    $! # Returns out of bar because it's invoked from a function
)

&&*bar~2~2 (
    $foo
)

$! # returns out of the program with exit code 0
>+ # COMPILE ERROR: Unreachable code (blame zig)
```
### Function Operators
|OP |Backtracking|Description
|---|---|---
|&\*name~*r*( )| | Defines a function with no args
|&\*name~*r*~*a*,*a*( )| | Defines a function with args
|&&\*name~*r*( )| | Defines and exports a function with no args
|&&\*name~*r*~*a*,*a*( )| | Defines and exports a function with args
|$*name|$$*name| Gets the address of a function
|$*n*|| Gets the address of a function argument
|$!|| Returns from a function with the value at the tape cursor
|&$name|&$$name|Finds and exports an existing function

## Multithreading
Multithreading exists as a language feature of Brainfuck++ because why not at this point? \

Threads can be created and stored by **ID** by the program. 
Functions defined with the signature `void(cell)` can be called directly by the **Spawn Thread** operator.

The **Spawn Thread** operator is a caret, a dollar sign, and a **name** `^$name`. It reads an **ID** from the tape saves a new thread to that **ID**. The function being called will be passed the thread's **ID** as an argument.

The **Join Thread** operator is a caret and an exclaimation mard `^!`. It reads a thread **ID** from the tape and waits for that thread to end execution. If the thread of that **ID** already ended then this operator does nothing.

```bfpp
&&*foo~1~20 (
    @"Hello from another thread!"
)

^$foo # Spawns a thread which runs the function foo

{
    ^$$foo # Works with backtracking.
}

^! # waits until foo is done
```
To make multithreading safer, mutexes can be used to coordinate data access.

Mutexes are objects stored by **ID** that only one thread can access at a time. If certain data is protected by a mutex then that data can only be modified by one thread at a time.

The **Create Mutex** operator is a caret and an ampersand `^&`. It reads an **ID** from the tape and saves a new mutex to that **ID**.

The **Mutex Block** operator is a caret before a pair of curly brackets `^{ }`. It reads a mutex **ID** from the tape and waits until that mutex is available to lock. before executing the contained code. When the contained code it finished executing, it unlocks the mutex for another thread to lock.
```bfpp
+2
^& # Creates a mutex with ID 2.

> +3
^$foo
<

# Creates a locking mutex block.
# Only one thread at a time can enter
# a mutex block of a given ID.
^{
    >2 ++ <2
}

> ^!

&*foo~1~20 (
    # If any thread enters any block with this mutex ID, then no other thread
    # may enter any block with this mutex until the first thread exits the block.
    ^{
        >2 -- <2
    }
    # While in this example both threads try to enter a mutex block with the
    # same ID (2) at the same time, only one is allowed in at a time.
    # A consistent order is not guaranteed.
)
```


### Multithreading Operators
|OP |Description
|---|---
|^$name| Spawns thread for a function
|^$$name| Spawns thread for a function, with backtracking
|^!| Joins a thread (waits for completion)
|^&| Creates a mutex
|^{ }| Defines a locking mutex block

## Table of Types
|Index|Type
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

## Project Configuration

Default project.toml file:
```toml
# The relative path to the main file / entry point.
main="main.bfpp"

# All outputs in this array will be generated on build.
outputs=["zig", "bf"]

## All options below are optional and set here to the default values.

# options in [common] apply to all outputs.
# These options can be added to the other
# sections to override for individual outputs.
[common]
include_comments = true
include_formatting = true

# Options for generating a zig project.
[zig]
args=[] # Args for the zig compiler
build_after_template = true
launch_after_build = true
cell_size = 1 # in bytes
#   Certain failures which would stop execution by default
#   can be ignored. Failures would simply not modify the tape.
# ignore_error = [
#     "findExternFunction",
#     "createExternCaller"
# ]

# Options for outputing a single processed .bfpp file.
[bf]
include_debug_operators=true
compact_operators=true # use numerable operators and for loops

[launch]
# Args for the compiled output
args=[]
```

### Outputs
Available values for `outputs`:
|Name|Description
|---|---
|zig|The main output type. Creates a zig project which can be compiled into a binary.
|bf|Consolidates the project into a single .bfpp file with all macros inlined and other syntax adjustments.

### Common Formatting
`include_comments` keeps the comments from the source `.bfpp` file and also adds additional comments to clarify obfuscated parts (e.g. function definitions and macro invocations are labeled). \
`include_formatting` adds whitespace to the output to make it more readable. Removing whitespace only really saves file size with the **bf** output. \
Zig does not support enabling `include_comments` without also enabling `include_formatting`.

### Cell Size
Throughout the reference document, cells are treated interchangably with bytes. This is accurate by default but when the `cell_size` is set differently, many of these descriptions become inaccurate.
Some parts of the language change when `cell_size` is set to a different number of bytes, and some don't change when they seem like they would:
* Any operator that writes one byte to the tape writes to the low byte.
* The **Print** operator `.` only reads the low byte of the current cell.
* The **Read** operator `,` only writes one byte to the tape.
* The **Dereference** operator `~` only writes one byte from the memory address to the tape.
* Non-byte types read from the tape only use as many cells as needed. e.g. Returning an i32 when cell_size is 2 will read the entirety of two cells, not one byte from four cells.
* Cell offsets within operators are reduced. e.g. Using the **Dereference** operator when cell_size is 4 will write the value to cell +2 instead of cell +8. This applies heavily to the **Call Extern Func** operator as well.

`cell_size` can only be set to 1, 2, 4, or 8.

### Compact Operators
Then building the bf output, changing `compact_operators` to false produces code as close to standard Brainfuck as possible. Numerable operators will be expanded (e.g. `+5` → `+++++`) and for loops will be flattened (e.g. `{3 +> }` → `+>+>+>`).

### IDE Integration - Launch Redirection
The `build` command in the terminal supports the flag `-redirectLaunch`, which stops the compiler from launching the project as a child process and insteads prints a launch code to the terminal. This is meant for an IDE to read so that it can manage the project executable directly. The code is only sent if `launch_after_build = true`.\
The launch code forwards the executable path, the directory it should be launched in, and the launch args from the project settings. This looks like `-Launch {"exe":"path/to/executable","cwd":"path/to/project","args":[]}`