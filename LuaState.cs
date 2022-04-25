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

    [AttributeUsage(AttributeTargets.Method)]
    public class LuaUserdataFunction : Attribute
    {
        public string Name;

        public LuaUserdataFunction(string name)
        {
            Name = name;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class LuaLibFunction : Attribute
    {
        public string Name;

        public LuaLibFunction(string name)
        {
            Name = name;
        }
    }
    public enum LuaType
    {
        Nil,
        Boolean,
        LightUserData,
        Number,
        String,
        Table,
        Function,
        UserData,
        Thread,
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
    public enum LuaResult
    {
        OK,
        ERRRUN,
        ERRFILE,
        ERRSYNTAX,
        ERRMEM,
        ERRERR
    }
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
        public string ShortSource = "";
    }

    public abstract class LuaState : IDisposable
    {
        public struct Reference
        {
            internal readonly int r;
            internal Reference(int i)
            {
                r = i;
            }
        }

        public abstract void Close();

        public void Dispose()
        {
            Close();
        }

        public abstract IntPtr State { get; protected set; }

        // basic stack funcs
        public abstract int REGISTRYINDEX { get; }
        public abstract int GLOBALSINDEX { get; }
        public abstract int UPVALUEINDEX(int i);

        public abstract int Top { get; set; }
        public abstract void CheckIndex(int i, bool acceptZero = false, bool acceptPseudo = true, bool intop = true);
        public abstract void CheckStack(int size);
        public abstract int ToAbsoluteIndex(int idx);
        public abstract void PushValue(int idx);
        public abstract void Pop(int i);
        public abstract void Remove(int i);
        public abstract void Insert(int i);
        public abstract void Replace(int i);

        // basic checks
        public abstract LuaType Type(int i);
        public abstract bool Equal(int i1, int i2);
        public abstract bool RawEqual(int i1, int i2);
        public abstract bool LessThan(int i1, int i2);
        public abstract bool IsNil(int i);
        public abstract bool IsBoolean(int i);
        public abstract bool IsNumber(int i);
        public abstract bool IsString(int i);
        public abstract bool IsTable(int i);
        public abstract bool IsFunction(int i);
        public abstract bool IsCFunction(int i);
        public abstract bool IsUserdata(int i);
        public abstract bool IsLightUserdata(int i);
        public abstract bool IsNoneOrNil(int i);

        // get values from stack
        public abstract bool ToBoolean(int i);
        public abstract double ToNumber(int ind);
        public abstract string ToString(int ind);
        public abstract IntPtr ToUserdata(int ind);
        public abstract IntPtr ToPointer(int idx);

        // push to stack
        public abstract void Push(bool b);
        public abstract void Push(double n);
        public abstract void Push(string s);
        public abstract void Push();
        public abstract void Push(Func<LuaState, int> f, int n = 0);
        public abstract void NewTable();
        protected abstract IntPtr NewUserdata(int size);

        // metatable / udata
        public abstract bool GetMetatable(int i);
        public abstract bool SetMetatable(int i);
        public abstract string GetMetatableEventString(MetaEvent e);
        public abstract void Push(MetaEvent e);
        public abstract bool CallMeta(int obj, string ev);
        public abstract bool CallMeta(int obj, MetaEvent ev);

        public abstract bool GetMetaField(int obj, string ev);
        public abstract bool GetMetaField(int obj, MetaEvent ev);
        public abstract void GetMetatableFromRegistry(string name);
        public abstract bool NewMetatable(string name);

        // tableaccess
        public abstract void GetTable(int i);
        public abstract void GetTableRaw(int i);
        public abstract void GetTableRaw(int i, int key);
        public abstract void SetTable(int i);
        public abstract void SetTableRaw(int i);
        public abstract void SetTableRaw(int i, int key);
        // iterate over table, key is at -2, value at -1, enumerable is type of value, only access them, dont change them
        public abstract IEnumerable<LuaType> Pairs(int i);
        // iterate over a table in array stile, from t[1] up to the first nil found, enumerable is index/key, -1 is value, only access them, dont change them
        public abstract IEnumerable<int> IPairs(int i);

        // calling
        public abstract void PCall(int nargs, int nres);
        public abstract LuaResult PCall_Debug(int nargs, int nres, int errfunc);

        // debug
        public abstract string ToDebugString(int i);
        public abstract DebugInfo GetStackInfo(int lvl, bool push = false);
        public abstract DebugInfo GetFuncInfo();
        public abstract int GetCurrentFuncStackSize();
        public abstract string GetStackTrace(int from = 0, int to = -1, string lineprefix = "");
        public abstract string GetLocalName(int lvl, int localnum);
        public abstract void GetLocal(int lvl, int localnum);
        public abstract void SetLocal(int lvl, int localnum);
        public abstract string GetUpvalueName(int funcidx, int upvalue);
        public abstract void GetUpvalue(int funcidx, int upvalue);
        public abstract void SetUpvalue(int funcidx, int upvalue);
        public abstract string Where(int lvl);
        public abstract void SetHook(Action<LuaState, DebugInfo> func, LuaHookMask mask, int count);

        // checks
        public abstract void ArgError(int arg, string msg);
        public abstract void TypeError(int i, string type);
        public abstract void TypeError(int i, LuaType t);
        public abstract void CheckAny(int i);
        public abstract double CheckNumber(int i);
        public abstract int CheckInt(int i);
        public abstract string CheckString(int i);
        public abstract bool CheckBool(int i);
        public abstract void CheckType(int i, params LuaType[] t);

        // load lua
        public abstract void LoadFile(string filename);
        public abstract void LoadBuffer(string code, string name);
        public abstract void DoFile(string filename);
        public abstract void DoString(string code, string name = null);
        public abstract int MULTIRETURN { get; }
        // opt
        public abstract double OptNumber(int idx, double def);
        public abstract int OptInt(int idx, int def);
        public abstract string OptString(int idx, string def);
        public abstract bool OptBool(int idx, bool def);
        // ref
        public abstract Reference Ref();
        public abstract void UnRef(Reference r);
        public abstract void Push(Reference r);
        public abstract Reference NOREF { get; }
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
        /// pushes a reference to an object as userdata to the lua stack.
        /// </summary>
        /// <typeparam name="T">object type, see GetUserDataMetatable</typeparam>
        /// <param name="ob">object to push</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void PushObjectAsUserdata<T>(T ob) where T : class
        {
            if (ob == null)
                throw new ArgumentNullException("ob null");
            IntPtr o = GCHandle.ToIntPtr(GCHandle.Alloc(ob));
            IntPtr ud = NewUserdata(Marshal.SizeOf<IntPtr>());
            Marshal.WriteIntPtr(ud, o);
            GetUserDataMetatable<T>();
            SetMetatable(-2);
        }

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
