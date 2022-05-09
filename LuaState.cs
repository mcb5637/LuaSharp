using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace LuaSharp
{
    /// <summary>
    /// an exception thrown by lua.
    /// </summary>
    public class LuaException : ApplicationException
    {
        public LuaException()
        {
        }

        public LuaException(string message) : base(message)
        {
        }

        public LuaException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected LuaException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }

    /// <summary>
    /// marks a function that should be added to a useedatas metatable via <see cref="LuaState.PushObjectAsUserdata"/>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LuaUserdataFunction : Attribute
    {
        public string Name;

        public LuaUserdataFunction(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// marks a function that should be exported to a libary via <see cref="LuaState.RegisterFuncLib"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class LuaLibFunction : Attribute
    {
        public string Name;

        public LuaLibFunction(string name)
        {
            Name = name;
        }
    }
    /// <summary>
    /// all values in lua are of one of these types
    /// </summary>
    public enum LuaType
    {
        /// <summary>
        /// represents no value (null).
        /// </summary>
        Nil,
        Boolean,
        LightUserData,
        Number,
        String,
        Table,
        Function,
        UserData,
        Thread,
        /// <summary>
        /// represents an unused stack position.
        /// </summary>
        None = -1,
    }
    public enum MetaEvent
    {
        Add,
        Subtract,
        Multiply,
        Divide,
        Pow,
        UnaryMinus,
        Concat,
        Equals,
        LessThan,
        LessOrEquals,
        Index,
        NewIndex,
        Call,
        Finalizer,
        WeakTable,
    };
    public enum LuaHook
    {
        Call,
        Ret,
        Line,
        Count,
        TailRet,
    }
    [Flags]
    public enum LuaHookMask
    {
        None = 0,
        Call = 1 << LuaHook.Call,
        Ret = 1 << LuaHook.Ret,
        Line = 1 << LuaHook.Line,
        Count = 1 << LuaHook.Count,
    }
    public class DebugInfo
    {
        public LuaHook Event;
        public string Name;
        public string NameWhat = "";
        public string What = "";
        public string Source = "";
        public int CurrentLine;
        public int NumUpvalues;
        public int LineDefined;
        public int LastLineDefined;
        public string ShortSource = "";
        public IntPtr ActivationRecord = IntPtr.Zero;
        public bool FreeAROnFinalize = false;
        ~DebugInfo()
        {
            if (FreeAROnFinalize && ActivationRecord != IntPtr.Zero)
                Marshal.FreeHGlobal(ActivationRecord);
        }
    }

    public static class StringMarshaler
    {
        public static Encoding EncodingUsed => Encoding.UTF8;
        public static string MarshalToString(this IntPtr p)
        {
            if (p == IntPtr.Zero)
                return null;
            int len = 0;
            while (Marshal.ReadByte(p, len) != 0) // strlen
                len++;
            return MarshalToString(p, len);
        }
        public static string MarshalToString(this IntPtr p, int len)
        {
            if (p == IntPtr.Zero)
                return null;
            byte[] buffer = new byte[len];
            Marshal.Copy(p, buffer, 0, len);
            return EncodingUsed.GetString(buffer);
        }
        public static StringPointerHolder MarshalFromString(this string s)
        {
            int len = EncodingUsed.GetByteCount(s);
            byte[] buff = new byte[len + 1];
            EncodingUsed.GetBytes(s, 0, s.Length, buff, 0);
            buff[buff.Length - 1] = 0;
            IntPtr r = Marshal.AllocHGlobal(buff.Length);
            Marshal.Copy(buff, 0, r, buff.Length);
            return new StringPointerHolder(r, len);
        }
        public class StringPointerHolder : IDisposable
        {
            public IntPtr String { get; private set; }
            public int Length { get; }

            internal StringPointerHolder(IntPtr s, int l)
            {
                String = s;
                Length = l;
            }

            ~StringPointerHolder()
            {
                Marshal.FreeHGlobal(String);
            }

            public void Dispose()
            {
                Marshal.FreeHGlobal(String);
                String = IntPtr.Zero;
                GC.SuppressFinalize(this);
            }
        }
    }

    public abstract class LuaState : IDisposable
    {
        /// <summary>
        /// a reference to a lua value (usually in the registry). see <see cref="Ref"/>
        /// </summary>
        public struct Reference
        {
            internal readonly int r;
            internal Reference(int i)
            {
                r = i;
            }
        }

        /// <summary>
        /// closes a lua state.
        /// Do not use the state for anything else after calling Close.
        /// </summary>
        public abstract void Close();

        /// <summary>
        /// just closes the state
        /// </summary>
        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// the internal state (may be used to pass to external functions).
        /// </summary>
        public abstract IntPtr State { get; protected set; }

        // basic stack funcs
        /// <summary>
        /// pseudoindex to access the global environment.
        /// </summary>
        public abstract int GLOBALSINDEX { get; }
        /// <summary>
        /// pseudoindex to access the registry.
        /// you can store lua values here that you want to access from C++ code, but should not be available to lua.
        /// use light userdata with adresses of something in your code, or strings prefixed with your library name as keys.
        /// integer keys are reserved for the Reference mechanism.
        /// <see cref="Ref"/>
        /// </summary>
        public abstract int REGISTRYINDEX { get; }
        /// <summary>
        /// returns the pseudoindex to access upvalue i.
        /// </summary>
        /// <param name="i">upvalue number</param>
        /// <returns>pseudoindex</returns>
        public abstract int UPVALUEINDEX(int i);
        /// <summary>
        /// passing this to call signals to return all values.
        /// </summary>
        public abstract int MULTIRETURN { get; }

        /// <summary>
        /// the current top of the lua stack.
        /// on increasing fills with nil, on decreasing disposes of opbects.
        /// may be set to any acceptable index.
        /// </summary>
        public abstract int Top { get; set; }
        /// <summary>
        /// <para>checks if a index represents a valid stack position.</para>
        /// <para>an index is valid, if it points to a stack position lower or equal to top (and not 0).</para>
        /// <para>an index is acceptable, if it points to a stack position lower or equal to the stack space (and not 0).</para>
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">index</param>
        /// <param name="acceptZero">is 0 valid</param>
        /// <param name="acceptPseudo">pseudoindexes valid</param>
        /// <param name="intop">check if in top</param>
        /// <exception cref="LuaException">on mismatch</exception>
        public abstract void CheckIndex(int i, bool acceptZero = false, bool acceptPseudo = true, bool intop = true);
        /// <summary>
        /// checks if the stack can grow to top + size elements.
        /// if it can do so, grows the stack and returns true. if not, returns false.
        /// <para>[-0,+0,m]</para>
        /// </summary>
        /// <param name="size">extra elements</param>
        /// <exception cref="LuaException">on cannot grow</exception>
        public abstract void CheckStack(int size);
        /// <summary>
        /// converts an index to a absolte index (not depending on the stack top position).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="idx">index</param>
        /// <returns>abs index</returns>
        public abstract int ToAbsoluteIndex(int idx);
        /// <summary>
        /// pushes a copy of something to the stack.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="idx">valid index to copy</param>
        public abstract void PushValue(int idx);
        /// <summary>
        /// pops num elements from the stack
        /// <para>[-num,+0,-]</para>
        /// </summary>
        /// <param name="i">amount to pop</param>
        public abstract void Pop(int i);
        /// <summary>
        /// removes the stack position index, and shifts elements down to fill the gap.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <param name="i">valid index to remove (no pseudoindex)</param>
        public abstract void Remove(int i);
        /// <summary>
        /// pops the ToS element and inserts it into index, shifting elements up to make a gap.
        /// <para>[-1,+1,-]</para>
        /// </summary>
        /// <param name="i">valid index to insert to (no pseudoindex)</param>
        public abstract void Insert(int i);
        /// <summary>
        /// pops the ToS element and replaces index with it.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <param name="i">valid index to replace</param>
        public abstract void Replace(int i);

        // basic checks
        /// <summary>
        /// returns the type of the index (or None if not valid).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptale index to check</param>
        /// <returns>type</returns>
        public abstract LuaType Type(int i);
        /// <summary>
        /// checks equality of 2 values. may call metamethods.
        /// <para>[-0,+0,e]</para>
        /// </summary>
        /// <param name="i1">acceptable index 1</param>
        /// <param name="i2">acceptable index 2</param>
        /// <returns>values equals</returns>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract bool Equal(int i1, int i2);
        /// <summary>
        /// checks primitive equality of 2 values. does not call metametods.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i1">acceptable index 1</param>
        /// <param name="i2">acceptable index 2</param>
        /// <returns>values equals</returns>
        public abstract bool RawEqual(int i1, int i2);
        /// <summary>
        /// checks if i1 is smaller than i2. may call metamethods.
        /// <para>[-0,+0,e]</para>
        /// </summary>
        /// <param name="i1">acceptable index 1</param>
        /// <param name="i2">acceptable index 2</param>
        /// <returns>smaller than</returns>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract bool LessThan(int i1, int i2);
        /// <summary>
        /// returns if the value at index is nil.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is nil</returns>
        public abstract bool IsNil(int i);
        /// <summary>
        /// returns if the value at index is of type boolean.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is bool</returns>
        public abstract bool IsBoolean(int i);
        /// <summary>
        /// returns if the value at index is a number or a string convertible to one.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is number</returns>
        public abstract bool IsNumber(int i);
        /// <summary>
        /// returns if the value at index is a string or a number (always cnvertible to string).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is string</returns>
        public abstract bool IsString(int i);
        /// <summary>
        /// returns if the value at index is of type table.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is table</returns>
        public abstract bool IsTable(int i);
        /// <summary>
        /// returns if the value at index is a function (C or lua).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is func</returns>
        public abstract bool IsFunction(int i);
        /// <summary>
        /// returns if the value at index is a C function.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is C func</returns>
        public abstract bool IsCFunction(int i);
        /// <summary>
        /// returns if the value at index is a userdata (full or light).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is userdata</returns>
        public abstract bool IsUserdata(int i);
        /// <summary>
        /// returns if the value at index is a light userdata.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is light userdata</returns>
        public abstract bool IsLightUserdata(int i);
        /// <summary>
        /// checks if the index is not valid (none) or the value at it is nil.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>is none or nil</returns>
        public abstract bool IsNoneOrNil(int i);

        // get values from stack
        /// <summary>
        /// converts the value at index to a boolean. nil, false and none evaluate to false, everything else (including 0) to true.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to convert</param>
        /// <returns>bool</returns>
        public abstract bool ToBoolean(int i);
        /// <summary>
        /// converts the value at index to a number. must be a number or a string convertible to a number, otherise returns 0.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="ind">acceptable index to convert</param>
        /// <returns>number</returns>
        public abstract double ToNumber(int ind);
        /// <summary>
        /// converts the value at index to a string. must be a string or a number, otherwise returns null.
        /// <para>warning: converts the value on the stack to a string, which might confuse pairs/next</para>
        /// <para>[-0,+0,m]</para>
        /// </summary>
        /// <param name="ind">acceptable index to convert</param>
        /// <returns>string</returns>
        public abstract string ToString(int ind);
        /// <summary>
        /// returns the data pointer of the userdata at index. returns the block adress of a full userdata, the pointer of a light userdata or nullptr.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="ind">acceptable index to convert</param>
        /// <returns>userdata pointer</returns>
        public abstract IntPtr ToUserdata(int ind);
        /// <summary>
        /// converts the value at index to a debugging pointer. must be a userdata, table, thread, or function, otherwise returns IntPtr.Zero.
        /// only useful for debugging information, cannot be converted back to its original value.
        /// guranteed to be different for different values.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="idx">acceptable index to convert</param>
        /// <returns>debug pointer</returns>
        public abstract IntPtr ToPointer(int idx);
        /// <summary>
        /// converts the value at index to a CFunction. must be a CFunction, otherwise returns IntPtr.Zero.
        /// To call it, you have to marshall it yourself. (or, even better use pcall).
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="idx">acceptable index to convert</param>
        /// <returns>CFunction</returns>
        public abstract IntPtr ToCFunction(int idx);
        /// <summary>
        /// returns the length of an object. for strings this is the number of bytes (==chars if each char is one byte).
        /// for tables this is one less than the first integer key with a nil value.
        /// for full userdata, it is the size of the allocated block of memory.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="index"></param>
        /// <returns>size</returns>
        public abstract int ObjLength(int index);

        // push to stack
        /// <summary>
        /// pushes a boolean onto the stack.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="b">bool</param>
        public abstract void Push(bool b);
        /// <summary>
        /// pushes a number onto the stack.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="n">number</param>
        public abstract void Push(double n);
        /// <summary>
        /// pushes a string onto the stack. the string ends at the first embedded 0.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="s">string</param>
        public abstract void Push(string s);
        /// <summary>
        /// pushes nil onto the stack.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        public abstract void Push();
        /// <summary>
        /// pushes a function or closure (function with upvalues) onto the stack.
        /// to create a closure, push the initial values for its upvalues onto the stack, and then call this function with the number of upvalues as nups.
        /// keeps the delegate alife, as long as it is reachable by lua by setting upvalue n+1 to a gc userdata that cleans up the delegate.
        /// <para>[-nups,+1,m]</para>
        /// </summary>
        /// <param name="f">function</param>
        /// <param name="n">number of upvalues</param>
        public abstract void Push(Func<LuaState, int> f, int n = 0);
        /// <summary>
        /// creates a new table and pushes it onto the stack.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        public abstract void NewTable();
        /// <summary>
        /// creates a new userdata and returns its adress. see <see cref="PushObjectAsUserdata"/>
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="size"></param>
        /// <returns>userdata pointer</returns>
        protected abstract IntPtr NewUserdata(int size);

        // metatable / udata
        /// <summary>
        /// pushes the metatable of the value at index and returns true if there is one. if there is no metatable pushes nothing and returns false.
        /// <para>[-0,+1|0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to get the metatable from</param>
        /// <returns>has metatable</returns>
        public abstract bool GetMetatable(int i);
        /// <summary>
        /// pops a value from the stack and sets it as the metatable of index.
        /// returns false, if it could not set the metatable, but still pops the value.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to set the metatable of</param>
        /// <returns>successfully set</returns>
        public abstract bool SetMetatable(int i);
        /// <summary>
        /// gets the string used for a metaevent.
        /// </summary>
        /// <param name="e">event</param>
        /// <returns>event string</returns>
        public abstract string GetMetatableEventString(MetaEvent e);
        /// <summary>
        /// pushes the string for a metaevent onto the stack.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="e"></param>
        public abstract void Push(MetaEvent e);
        /// <summary>
        /// if obj has a metatable and a field ev in it, calls it with obj as its only argument and pushes its return value.
        /// returns if it found a method to call.
        /// <para>[-0,+0|1,e]</para>
        /// </summary>
        /// <param name="obj">valid index to call methamethod of</param>
        /// <param name="ev">event string</param>
        /// <returns>found method</returns>
        public abstract bool CallMeta(int obj, string ev);
        /// <summary>
        /// if obj has a metatable and a field ev in it, calls it with obj as its only argument and pushes its return value.
        /// returns if it found a method to call.
        /// <para>[-0,+0|1,e]</para>
        /// </summary>
        /// <param name="obj">valid index to call methamethod of</param>
        /// <param name="ev">event</param>
        /// <returns>found method</returns>
        public abstract bool CallMeta(int obj, MetaEvent ev);

        /// <summary>
        /// pushes the metafield of obj onto the stack.
        /// returns if it found one, pushes nothing if not.
        /// <para>[-0,+1|0,m]</para>
        /// </summary>
        /// <param name="obj">object to check</param>
        /// <param name="ev">event name</param>
        /// <returns>found</returns>
        public abstract bool GetMetaField(int obj, string ev);
        /// <summary>
        /// pushes the metafield of obj onto the stack.
        /// returns if it found one, pushes nothing if not.
        /// <para>[-0,+1|0,m]</para>
        /// </summary>
        /// <param name="obj">object to check</param>
        /// <param name="ev">event</param>
        /// <returns>found</returns>
        public abstract bool GetMetaField(int obj, MetaEvent ev);
        /// <summary>
        /// pushes the metatable associated with name.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="name">metatable name</param>
        public abstract void GetMetatableFromRegistry(string name);
        /// <summary>
        /// if the registry already has a value associated with name, returns 0. otherwise creates a new table, adds it and returns 1.
        /// in both cases, pushes the final value onto the stack.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="name">metatable name</param>
        /// <returns>created</returns>
        public abstract bool NewMetatable(string name);

        // tableaccess
        /// <summary>
        /// pops a key from the stack, and pushes the associated value in the table at index onto the stack.
        /// may call metamethods.
        /// <para>[-1,+1,e]</para>
        /// </summary>
        /// <param name="i">valid index for table access</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void GetTable(int i);
        /// <summary>
        /// pops a key from the stack, and pushes the associated value in the table at index onto the stack.
        /// may not call metamethods.
        /// <para>[-1,+1,-]</para>
        /// </summary>
        /// <param name="i">valid index for table access</param>
        public abstract void GetTableRaw(int i);
        /// <summary>
        /// pushes the with n associated value in the table at index onto the stack.
        /// may not call metamethods.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="i">valid index for table access</param>
        /// <param name="key">key</param>
        public abstract void GetTableRaw(int i, int key);
        /// <summary>
        /// assigns the value at the top of the stack to the key just below the top in the table at index. pops both key and value from the stack.
        /// may call metamethods.
        /// <para>[-2,+0,e]</para>
        /// </summary>
        /// <param name="i">valid index for table acccess</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void SetTable(int i);
        /// <summary>
        /// assigns the value at the top of the stack to the key just below the top in the table at index. pops both key and value from the stack.
        /// may not call metamethods.
        /// <para>[-2,+0,m]</para>
        /// </summary>
        /// <param name="i">valid index for table acccess</param>
        public abstract void SetTableRaw(int i);
        /// <summary>
        /// assigns the value at the top of the stack to the key n in the table at index. pops the value from the stack.
        /// may not call metamethods.
        /// <para>[-1,+0,m]</para>
        /// </summary>
        /// <param name="i">valid index for table acccess</param>
        /// <param name="key">key</param>
        public abstract void SetTableRaw(int i, int key);
        /// <summary>
        /// traverses the table index by poping the previous key from the stack and pushing the next key and value to the stack.
        /// if there are no more elements in the table, returns false and pushes nothing. otherwise returns true.
        /// <para>do not call tostring onto a key, unless you know that it is acually a string</para>
        /// <para>[-1,+2|0,e]</para>
        /// </summary>
        /// <param name="i">valid index to traverse</param>
        /// <returns>had next</returns>
        protected abstract bool Next(int i);
        /// <summary>
        /// <para>allows iteration over a lua table.</para>
        /// <para>while iterating, the key is at -2, and the value is at -1.</para>
        /// <para>do not pop value or key.</para>
        /// <para>do not apply ToString directly onto the key, unless you know it is actually a string.</para>
        /// <para>iterator returns the type of the key.</para>
        /// <para>when the iteration ends by reaching its end, no key/value pair is left on the stack.</para>
        /// <para>if you break the iteration (or leave it otherwise), you have to clean up the key/value pair from the stack yourself.</para>
        /// <para>[-0,+2|0,e]</para>
        /// </summary>
        /// <param name="i">valid index to iterate over</param>
        /// <returns>type enum</returns>
        /// <exception cref="LuaException">on lua error</exception>
        public IEnumerable<LuaType> Pairs(int i)
        {
            int t = Top + 1;
            i = ToAbsoluteIndex(i);
            CheckIndex(i);
            CheckStack(2);
            Push();
            while (Next(i))
            {
                yield return Type(-1);
                Pop(1); // remove val, keep key for next
                if (Top != t)
                    throw new LuaException("pairs stack top mismatch");
            }
            // after traversal no key gets pushed
        }
        /// <summary>
        /// <para>allows iteration over an array style lua table.</para>
        /// <para>while iterating the key is in the iterator, and the value is at -1.</para>
        /// <para>the iteration begins at key 1 and ends directly before the first key that is assigned nil.</para>
        /// <para>when the iteration ends by reaching its end, no value is left on the stack.</para>
        /// <para>if you break the iteration (or leave it otherwise), you have to clean up the value from the stack yourself.</para>
        /// <para>[-0,+1|0,-]</para>
        /// </summary>
        /// <param name="i">valid index to iterate over</param>
        /// <returns>key enum</returns>
        public IEnumerable<int> IPairs(int i)
        {
            int t = Top;
            CheckIndex(i);
            CheckStack(1);
            int ind = 1;
            while (true)
            {
                GetTableRaw(i, ind);
                if (Type(-1) == LuaType.Nil)
                {
                    Pop(1);
                    break;
                }
                yield return ind;
                Pop(1);
                if (Top != t)
                    throw new LuaException("ipairs stack top mismatch");
                ind++;
            }
        }

        // calling
        /// <summary>
        /// calls a function. does catch lua exceptions, and throws an LuaException.
        /// first push the function, then the arguments in order, then call.
        /// pops the function and its arguments, then pushes its results.
        /// use MULTIRET to return all values, use GetTop tofigure out how many got returned.
        /// if an error gets cought, attaches a stack trace and then throws a LuaException.
        /// <para>[-nargs+1,+nresults|0,-]</para>
        /// </summary>
        /// <param name="nargs">number of parameters</param>
        /// <param name="nres">number of return values</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void PCall(int nargs, int nres);
        /// <summary>
        /// calls a function. does catch lua errors and returns an error code. see <see cref="PCall"/>
        /// </summary>
        /// <param name="nargs">number of parameters</param>
        /// <param name="nres">number of results</param>
        /// <param name="errfunc">index of error func, or 0</param>
        /// <returns>error code</returns>
        public abstract int PCall_Debug(int nargs, int nres, int errfunc);

        // debug
        /// <summary>
        /// turns the value at index to a debug string.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>debug string</returns>
        public abstract string ToDebugString(int i);
        /// <summary>
        /// gets the debug info for a stack level.
        /// stack level 0 is the current running function, n+1 is the function that called n.
        /// <para>[-0,+0|1,-]</para>
        /// </summary>
        /// <param name="lvl">stack level to query</param>
        /// <param name="push">push the running function onto the stack.</param>
        /// <returns>level valid</returns>
        public abstract DebugInfo GetStackInfo(int lvl, bool push = false);
        /// <summary>
        /// gets the debug info for a function at ToS.
        /// Pops the function.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <returns>debug info</returns>
        public abstract DebugInfo GetFuncInfo();
        /// <summary>
        /// pushes the function of a debuginfo.
        /// </summary>
        /// <param name="i">debug info</param>
        public abstract void PushDebugInfoFunc(DebugInfo i);
        /// <summary>
        /// gets the size of the current execution stack.
        /// </summary>
        /// <returns>stack size</returns>
        public abstract int GetCurrentFuncStackSize();
        /// <summary>
        /// generates a stack trace from levelStart to levelEnd (or the end of the stack).
        /// level 0 is the current running function, and n+1 is the function that called n.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="from">stack level to start</param>
        /// <param name="to">stack level to end (may end before that, if the end of the stack is reached)</param>
        /// <param name="lineprefix">prefix for each line</param>
        /// <returns>stack trace</returns>
        public abstract string GetStackTrace(int from = 0, int to = -1, string lineprefix = "");
        /// <summary>
        /// gets the name of a local variable.
        /// returns null if not existent.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="i">debuginfo</param>
        /// <param name="localnum">local number (1 based)</param>
        /// <returns>name or null</returns>
        public abstract string GetLocalName(DebugInfo i, int localnum);
        /// <summary>
        /// gets the value of a local variable.
        /// throws if not existent.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="i">debuginfo</param>
        /// <param name="localnum">local number (1 based)</param>
        public abstract void GetLocal(DebugInfo i, int localnum);
        /// <summary>
        /// sets the value of a local variable.
        /// throws if not existent.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <param name="i">debuginfo</param>
        /// <param name="localnum">local number (1 based)</param>
        public abstract void SetLocal(DebugInfo i, int localnum);
        /// <summary>
        /// gets the name of a upvalue.
        /// returns null if not existent.
        /// for C functions, uses the empty string as name for all valid upvalues.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="funcidx">valid index of function</param>
        /// <param name="upvalue">upvalue number (1 based)</param>
        /// <returns>name or null</returns>
        public abstract string GetUpvalueName(int funcidx, int upvalue);
        /// <summary>
        /// gets the value of a upvalue.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="funcidx">valid index of function</param>
        /// <param name="upvalue">upvalue number (1 based)</param>
        /// <returns>name or null</returns>
        public abstract void GetUpvalue(int funcidx, int upvalue);
        /// <summary>
        /// sets the value of a upvalue.
        /// <para>[-1,+0,-]</para>
        /// </summary>
        /// <param name="funcidx">valid index of function</param>
        /// <param name="upvalue">upvalue number (1 based)</param>
        /// <returns>name or null</returns>
        public abstract void SetUpvalue(int funcidx, int upvalue);
        /// <summary>
        /// used to build a prefix for error messages, pushes a string of the form 'chunkname:currentline: '
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="lvl">stack level</param>
        public abstract string Where(int lvl);
        /// <summary>
        /// sets/unsets a lua hook. call with func = null or mask = LuaHookMask.None to remove.
        /// Note: gets autoatically removed when the LuaState object gets GC'd.
        /// catches all exceptions thrown, to not let then propagate through lua (which would break stuff).
        /// </summary>
        /// <param name="func">hook function or null</param>
        /// <param name="mask">mask</param>
        /// <param name="count">count</param>
        public abstract void SetHook(Action<LuaState, DebugInfo> func, LuaHookMask mask, int count);

        // checks
        /// <summary>
        /// generates an error message of the form
        /// 'bad argument #&lt;arg&gt; to &lt;func&gt; (&lt;extramsg&gt;)'
        /// throws c++ or lua error depending on CatchExceptions.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="arg">argument number</param>
        /// <param name="msg">extra message</param>
        /// <exception cref="LuaException">always</exception>
        public abstract void ArgError(int arg, string msg);
        /// <summary>
        /// throws an error with the message: "location: bad argument narg to 'func' (tname expected, got rt)"
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">index</param>
        /// <param name="type">expected type</param>
        /// <exception cref="LuaException">always</exception>
        public abstract void TypeError(int i, string type);
        /// <summary>
        /// throws an error with the message: "location: bad argument narg to 'func' (tname expected, got rt)"
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">index</param>
        /// <param name="t">expected type</param>
        /// <exception cref="LuaException">always</exception>
        public abstract void TypeError(int i, LuaType t);
        /// <summary>
        /// checks if there is any argument including nil) at idx
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <exception cref="LuaException">if none</exception>
        public abstract void CheckAny(int i);
        /// <summary>
        /// checks if there is a number and returns it.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>number</returns>
        /// <exception cref="LuaException">if not number</exception>
        public abstract double CheckNumber(int i);
        /// <summary>
        /// checks if there is a number and returns it cast to a int.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>int</returns>
        /// <exception cref="LuaException">if not number</exception>
        public abstract int CheckInt(int i);
        /// <summary>
        /// checks if there is a string and returns it.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>string</returns>
        /// <exception cref="LuaException">if not string</exception>
        public abstract string CheckString(int i);
        /// <summary>
        /// checks if there is a bool and returns it.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <returns>bool</returns>
        /// <exception cref="LuaException">if not bool</exception>
        public abstract bool CheckBool(int i);
        /// <summary>
        /// checks the type if idx.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="i">acceptable index to check</param>
        /// <param name="t">types</param>
        /// <exception cref="LuaException">if type does not match</exception>
        public abstract void CheckType(int i, params LuaType[] t);

        // load lua
        /// <summary>
        /// loads a file as lua code and leaves it on the stack to execute.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="filename">file name</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void LoadFile(string filename);
        /// <summary>
        /// loads a buffer as lua code and leaves it on the stack to execute.
        /// <para>[-0,+1,m]</para>
        /// </summary>
        /// <param name="code">lua code</param>
        /// <param name="name">code name</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void LoadBuffer(string code, string name);
        /// <summary>
        /// loads a file as lua code and executes it.
        /// <para>[-0,+?,m]</para>
        /// </summary>
        /// <param name="filename">file name</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void DoFile(string filename);
        /// <summary>
        /// loads a string as lua code and executes it.
        /// <para>[-0,+?,m]</para>
        /// </summary>
        /// <param name="code">lua code</param>
        /// <param name="name">code name</param>
        /// <exception cref="LuaException">on lua error</exception>
        public abstract void DoString(string code, string name = null);
        // opt
        /// <summary>
        /// in idx is a number returns it. if idx is none or nil, returns def.
        /// otherwise throws.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="idx">aceptable index to check</param>
        /// <param name="def">default value</param>
        /// <returns>number</returns>
        /// <exception cref="LuaException">if invalid</exception>
        public abstract double OptNumber(int idx, double def);
        /// <summary>
        /// in idx is a number returns it cast to an int. if idx is none or nil, returns def.
        /// otherwise throws.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="idx">aceptable index to check</param>
        /// <param name="def">default value</param>
        /// <returns>int</returns>
        /// <exception cref="LuaException">if invalid</exception>
        public abstract int OptInt(int idx, int def);
        /// <summary>
        /// in idx is a string returns. if idx is none or nil, returns def.
        /// otherwise throws.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="idx">aceptable index to check</param>
        /// <param name="def">default value</param>
        /// <returns>string</returns>
        /// <exception cref="LuaException">if invalid</exception>
        public abstract string OptString(int idx, string def);
        /// <summary>
        /// in idx is a bool returns it. if idx is none or nil, returns def.
        /// otherwise throws.
        /// <para>[-0,+0,v]</para>
        /// </summary>
        /// <param name="idx">aceptable index to check</param>
        /// <param name="def">default value</param>
        /// <returns>bool</returns>
        /// <exception cref="LuaException">if invalid</exception>
        public abstract bool OptBool(int idx, bool def);
        // ref
        /// <summary>
        /// creates a unique reference to a value.
        /// pops the value.
        /// <para>[-1,+0,m]</para>
        /// </summary>
        /// <returns>reference</returns>
        public abstract Reference Ref();
        /// <summary>
        /// frees the reference r.
        /// <para>[-0,+0,-]</para>
        /// </summary>
        /// <param name="r">reference</param>
        public abstract void UnRef(Reference r);
        /// <summary>
        /// pushes the value associated with the reference r.
        /// <para>[-0,+1,-]</para>
        /// </summary>
        /// <param name="r">reference</param>
        public abstract void Push(Reference r);
        /// <summary>
        /// no valid reference, guranteed to be different from all valid references.
        /// if pushed, pushes nil.
        /// </summary>
        public abstract Reference NOREF { get; }
        /// <summary>
        /// reference to nil.
        /// </summary>
        public abstract Reference REFNIL { get; }

        // objects as userdata
        private static readonly string TypeNameName = "IsCSharpObject";
        /// <summary>
        /// creates the metatable for a userdata of type T.
        /// registers all methods marked with LuaUserdataFunction as lua functions to __index (the object is automatically recovered from the 1st parameter).
        /// </summary>
        /// <typeparam name="T">type for the metatable</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public void GetUserDataMetatable<T>() where T : class
        {
            string name = typeof(T).FullName;
            if (name == null)
                throw new ArgumentNullException("T");
            if (NewMetatable(name))
            {
                Push(TypeNameName);
                Push(name);
                SetTableRaw(-3);

                Push(MetaEvent.Index);
                NewTable();
                foreach (MethodInfo m in typeof(T).GetMethods())
                {
                    LuaUserdataFunction f = m.GetCustomAttribute<LuaUserdataFunction>();
                    if (f != null)
                    {
                        Func<T, LuaState, int> methoddel = (Func<T, LuaState, int>)Delegate.CreateDelegate(typeof(Func<T, LuaState, int>), m);
                        Push(f.Name);
                        Push((LuaState s) =>
                        {
                            T o = s.CheckUserdata<T>(1);
                            return methoddel(o, s);
                        });
                        SetTableRaw(-3);
                    }
                }
                SetTableRaw(-3);

                Push(MetaEvent.Finalizer);
                Push((LuaState s) =>
                {
                    IntPtr ud = ToUserdata(1);
                    IntPtr o = Marshal.ReadIntPtr(ud);
                    GCHandle.FromIntPtr(o).Free();
                    return 0;
                });
                SetTableRaw(-3);
            }
        }

        /// <summary>
        /// pushes a reference to an object as userdata to the lua stack. see <see cref="GetUserDataMetatable"/>
        /// </summary>
        /// <typeparam name="T">object type</typeparam>
        /// <param name="ob">object to push</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void PushObjectAsUserdata<T>(T ob) where T : class
        {
            if (ob == null)
                throw new ArgumentNullException(nameof(ob));
            IntPtr o = GCHandle.ToIntPtr(GCHandle.Alloc(ob));
            IntPtr ud = NewUserdata(Marshal.SizeOf<IntPtr>());
            Marshal.WriteIntPtr(ud, o);
            GetUserDataMetatable<T>();
            SetMetatable(-2);
        }

        /// <summary>
        /// checks if i is a userdata of type T and returns it.
        /// returns null otherwise.
        /// see <see cref="GetUserDataMetatable"/>
        /// </summary>
        /// <typeparam name="T">object type</typeparam>
        /// <param name="i">valid index to check</param>
        /// <returns>object</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public T OptionalUserData<T>(int i) where T : class
        {
            string name = typeof(T).FullName;
            if (name == null)
                throw new ArgumentNullException("T");

            CheckIndex(i);
            GetMetatable(i);

            Push(TypeNameName);
            GetTableRaw(-2);
            if (Type(-1) == LuaType.String)
            {
                Pop(1);
                IntPtr ud = ToUserdata(i);
                IntPtr o = Marshal.ReadIntPtr(ud);
                return GCHandle.FromIntPtr(o).Target as T;
            }
            Pop(1);
            return default;
        }
        /// <summary>
        /// checks if i is a userdata of type T and returns it.
        /// throws otherwise.
        /// </summary>
        /// <typeparam name="T">object type</typeparam>
        /// <param name="i">valid index to check</param>
        /// <returns>object</returns>
        /// <exception cref="LuaException">if invalid</exception>
        public T CheckUserdata<T>(int i) where T : class
        {
            T o = OptionalUserData<T>(i);
            if (o == null)
                TypeError(i, typeof(T).FullName);
            return o;
        }

        /// <summary>
        /// registers all static methods marked with LuaLibFunction.
        /// use GLOBALSINDEX to register as globals or -3 to register into a table at the top of the stack
        /// </summary>
        /// <typeparam name="T">class that holds the functions to register</typeparam>
        /// <param name="idx">register to</param>
        public void RegisterFuncLib<T>(int idx)
        {
            foreach (MethodInfo m in typeof(T).GetMethods())
            {
                LuaLibFunction f = m.GetCustomAttribute<LuaLibFunction>();
                if (f != null)
                {
                    Func<LuaState, int> methoddel = (Func<LuaState, int>)Delegate.CreateDelegate(typeof(Func<LuaState, int>), m);
                    Push(f.Name);
                    Push(methoddel);
                    SetTableRaw(idx);
                }
            }
        }
    }
}
