using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace LuaSharp
{
    public class LuaState50 : LuaState
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct LuaDebugRecord
        {
            public LuaHook debugEvent;
            public IntPtr name; /* (n) */
            public IntPtr namewhat; /* (n) `global', `local', `field', `method' */
            public IntPtr what; /* (S) `Lua', `C', `main', `tail' */
            public IntPtr source;   /* (S) */
            public int currentline; /* (l) */
            public int nups;        /* (u) number of upvalues */
            public int linedefined; /* (S) */
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 60)]
            public string short_src; /* (S) */

            /* private part */
            private int privateInt;  /* active function */
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int LuaCFunc(IntPtr L);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void LuaHookFunc(IntPtr L, IntPtr debugrecord);

        public const string LuaDll = "lua50/lua50.dll";

        [DllImport(LuaDll, EntryPoint = "lua_open", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_open();
        [DllImport(LuaDll, EntryPoint = "lua_close", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_close(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_base", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_base(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_string", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_string(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_table", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_table(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_math", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_math(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_io", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_io(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "luaopen_debug", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Luaopen_debug(IntPtr l);

        public override IntPtr State { get; protected set; }
        private bool AutoClose;
        private int CurrentUpvalues = 0;
        public LuaState50(IntPtr s, bool autoClose = false)
        {
            State = s;
            AutoClose = autoClose;
            PrepareFuncUDMeta();
        }
        public LuaState50()
        {
            State = Lua_open();
            AutoClose = true;
            PrepareFuncUDMeta();
            Luaopen_base(State);
            Luaopen_string(State);
            Luaopen_table(State);
            Luaopen_math(State);
            Luaopen_io(State);
            Luaopen_debug(State);
        }
        ~LuaState50()
        {
            if (!closed && HookFunc != null)
                SetHook(null, LuaHookMask.None, 0);
            if (AutoClose)
                Lua_close(State);
        }

        private bool closed = false;
        public override void Close()
        {
            Lua_close(State);
            AutoClose = false;
            HookFunc = null;
            closed = true;
        }


        // error funcs
        [DllImport(LuaDll, EntryPoint = "lua_error", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_error(IntPtr l);
        private void CheckError(LuaResult r)
        {
            if (r != LuaResult.OK)
            {
                string s = ToString(-1);
                Pop(1);
                throw new LuaException(s ?? "unknown lua exception");
            }
        }


        // basic stack funcs
        [DllImport(LuaDll, EntryPoint = "lua_gettop", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_gettop(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "lua_settop", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_settop(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_checkstack", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_checkstack(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_pushvalue", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushvalue(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_remove", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_remove(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_insert", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_insert(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_replace", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_replace(IntPtr l, int i);

        public override int REGISTRYINDEX => -10000;
        public override int GLOBALSINDEX => -10001;
        public override int UPVALUEINDEX(int i)
        {
            if (i <= 0)
                throw new LuaException("invalid upvalueindex");
            return GLOBALSINDEX - i;
        }

        public override int Top
        {
            get => Lua_gettop(State);
            set
            {
                CheckIndex(value, true, false, false);
                Lua_settop(State, value);
            }
        }
        public override void CheckIndex(int i, bool acceptZero = false, bool acceptPseudo = true, bool intop = true)
        {
            int top = Top;
            if (i < 0)
            {
                if (-i <= top)
                    return;
                if (acceptZero && -i == top + 1)
                    return;
                if (acceptPseudo)
                {
                    if (i <= REGISTRYINDEX && i >= (GLOBALSINDEX - CurrentUpvalues))
                        return;
                }
                throw new LuaException($"index {i} is no valid stack pos or pseudoindex");
            }
            else if (i > 0)
            {
                if (i > top)
                {
                    if (intop)
                        throw new LuaException($"index {i} is not currently used");
                    else
                        CheckStack(i - top);
                }
            }
            else if (!acceptZero)
                throw new LuaException("index is 0");
        }

        public override void CheckStack(int size)
        {
            if (Lua_checkstack(State, size) == 0)
                throw new LuaException($"lua stack overflow at size {size}");
        }
        public override int ToAbsoluteIndex(int idx)
        {
            if (idx > 0)
                return idx;
            if (idx <= REGISTRYINDEX)
                return idx;
            return Top + idx + 1;
        }
        public override void PushValue(int idx)
        {
            CheckIndex(idx, false, false, true);
            CheckStack(1);
            Lua_pushvalue(State, idx);
        }
        public override void Pop(int i)
        {
            Top = -i - 1;
        }
        public override void Remove(int i)
        {
            CheckIndex(i, false, false, true);
            Lua_remove(State, i);
        }
        public override void Insert(int i)
        {
            CheckIndex(i, false, false, true);
            Lua_insert(State, i);
        }
        public override void Replace(int i)
        {
            CheckIndex(i, false, false, true);
            Lua_replace(State, i);
        }

        // bacic checks
        [DllImport(LuaDll, EntryPoint = "lua_type", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaType Lua_type(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_equal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_equal(IntPtr l, int i, int i2);
        [DllImport(LuaDll, EntryPoint = "lua_rawequal", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_rawequal(IntPtr l, int i, int i2);
        [DllImport(LuaDll, EntryPoint = "lua_lessthan", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_lessthan(IntPtr l, int i, int i2);
        [DllImport(LuaDll, EntryPoint = "lua_isnumber", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_isnumber(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_isstring", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_isstring(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_iscfunction", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_iscfunction(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_isuserdata", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_isuserdata(IntPtr l, int i);
        public override LuaType Type(int i)
        {
            CheckIndex(i);
            return Lua_type(State, i);
        }
        public override bool Equal(int i1, int i2)
        {
            CheckIndex(i1);
            CheckIndex(i2);
            return Lua_equal(State, i1, i2) != 0;
        }
        public override bool RawEqual(int i1, int i2)
        {
            CheckIndex(i1);
            CheckIndex(i2);
            return Lua_rawequal(State, i1, i2) != 0;
        }
        public override bool LessThan(int i1, int i2)
        {
            CheckIndex(i1);
            CheckIndex(i2);
            return Lua_lessthan(State, i1, i2) != 0;
        }
        public override bool IsNil(int i)
        {
            return Type(i) == LuaType.Nil;
        }
        public override bool IsBoolean(int i)
        {
            return Type(i) == LuaType.Boolean;
        }
        public override bool IsNumber(int i)
        {
            CheckIndex(i);
            return Lua_isnumber(State, i) != 0;
        }
        public override bool IsString(int i)
        {
            CheckIndex(i);
            return Lua_isstring(State, i) != 0;
        }
        public override bool IsTable(int i)
        {
            return Type(i) == LuaType.Table;
        }
        public override bool IsFunction(int i)
        {
            return Type(i) == LuaType.Function;
        }
        public override bool IsCFunction(int i)
        {
            CheckIndex(i);
            return Lua_iscfunction(State, i) != 0;
        }
        public override bool IsUserdata(int i)
        {
            CheckIndex(i);
            return Lua_isuserdata(State, i) != 0;
        }
        public override bool IsLightUserdata(int i)
        {
            return Type(i) == LuaType.LightUserData;
        }
        public override bool IsNoneOrNil(int i)
        {
            CheckIndex(i, false, true, false);
            LuaType t = Lua_type(State, i);
            return t == LuaType.Nil || t == LuaType.None;
        }

        // get values from stack
        [DllImport(LuaDll, EntryPoint = "lua_toboolean", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_toboolean(IntPtr l, int ind);
        [DllImport(LuaDll, EntryPoint = "lua_tonumber", CallingConvention = CallingConvention.Cdecl)]
        private static extern double Lua_tonumber(IntPtr l, int ind);
        [DllImport(LuaDll, EntryPoint = "lua_tostring", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_tostring(IntPtr l, int ind);
        [DllImport(LuaDll, EntryPoint = "lua_tocfunction", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_tocfunction(IntPtr l, int ind);
        [DllImport(LuaDll, EntryPoint = "lua_touserdata", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_touserdata(IntPtr l, int ind);
        [DllImport(LuaDll, EntryPoint = "lua_topointer", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_topointer(IntPtr l, int ind);
        // tothread
        public override bool ToBoolean(int i)
        {
            CheckIndex(i);
            return Lua_toboolean(State, i) != 0;
        }
        public override double ToNumber(int ind)
        {
            CheckIndex(ind);
            return Lua_tonumber(State, ind);
        }
        // warning: converts value on the stack to a string
        public override string ToString(int ind)
        {
            CheckIndex(ind);
            IntPtr ptr = Lua_tostring(State, ind);
            if (ptr == IntPtr.Zero)
                return null;
            return Marshal.PtrToStringAnsi(ptr);
        }
        public override IntPtr ToUserdata(int ind)
        {
            CheckIndex(ind);
            return Lua_touserdata(State, ind);
        }
        public override IntPtr ToPointer(int idx)
        {
            CheckIndex(idx);
            return Lua_topointer(State, idx);
        }
        public override IntPtr ToCFunction(int idx)
        {
            if (!IsCFunction(idx))
                TypeError(idx, "CFunction");
            return Lua_tocfunction(State, idx);
        }
        // cfunc, thread, pointer


        // push to stack
        [DllImport(LuaDll, EntryPoint = "lua_pushboolean", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushboolean(IntPtr l, int n);
        [DllImport(LuaDll, EntryPoint = "lua_pushnumber", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushnumber(IntPtr l, double n);
        [DllImport(LuaDll, EntryPoint = "lua_pushstring", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushstring(IntPtr l, IntPtr p);
        [DllImport(LuaDll, EntryPoint = "lua_pushnil", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushnil(IntPtr l);
        [DllImport(LuaDll, EntryPoint = "lua_pushcclosure", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushcclosure(IntPtr l, IntPtr f, int n);
        [DllImport(LuaDll, EntryPoint = "lua_pushlightuserdata", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_pushlightuserdata(IntPtr l, IntPtr p);
        [DllImport(LuaDll, EntryPoint = "lua_newtable", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_newtable(IntPtr l);
        public override void Push(bool b)
        {
            CheckStack(1);
            Lua_pushboolean(State, b ? 1 : 0);
        }
        public override void Push(double n)
        {
            CheckStack(1);
            Lua_pushnumber(State, n);
        }
        public override void Push(string s)
        {
            CheckStack(1);
            IntPtr b = Marshal.StringToHGlobalAnsi(s);
            try
            {
                Lua_pushstring(State, b);
            }
            finally
            {
                Marshal.FreeHGlobal(b);
            }
        }
        public override void Push()
        {
            CheckStack(1);
            Lua_pushnil(State);
        }
        public override void NewTable()
        {
            CheckStack(1);
            Lua_newtable(State);
        }
        protected override IntPtr NewUserdata(int size)
        {
            return Lua_newuserdata(State, size);
        }
        private static readonly string FuncUDMeta = "LuaSharpFuncUDMeta";
        private static readonly LuaCFunc FuncUDGC = (IntPtr p) => {
            LuaState50 s = new LuaState50(p);
            IntPtr ud = Lua_touserdata(p, s.UPVALUEINDEX(1));
            IntPtr h = Marshal.ReadIntPtr(ud);
            GCHandle.FromIntPtr(h).Free();
            return 0;
        };
        private void PrepareFuncUDMeta()
        {
            Push(FuncUDMeta);
            GetTableRaw(REGISTRYINDEX);
            if (IsNil(-1))
            {
                Pop(1);
                return;
            }
            Pop(1);
            Push(FuncUDMeta);
            NewTable();
            Push(MetaEvent.Finalizer);
            Lua_pushcclosure(State, Marshal.GetFunctionPointerForDelegate(FuncUDGC), 0);
            SetTableRaw(-3);
            SetTableRaw(REGISTRYINDEX);
        }
        // upvalue n+1 of any func is always a gc userdata that cleans up the delegate
        public override void Push(Func<LuaState, int> f, int n = 0)
        {
            if (Top < n)
                throw new LuaException("not enough upvalues for c closure");
            CheckStack(2);
            LuaCFunc p = (IntPtr pt) =>
            {
                LuaState50 s = new LuaState50(pt);
                s.CurrentUpvalues = n;
                try
                {
                    int i = f(s);
                    if (i > Top)
                        throw new LuaException("c func has noth enough values on the stack for return");
                    return i;
                }
                catch (Exception e)
                {
                    s.Push(e.ToString());
                    s.ToString(-1);
                    Lua_error(pt);
                    return 0;
                }
            };
            IntPtr h = GCHandle.ToIntPtr(GCHandle.Alloc(p));
            IntPtr ud = Lua_newuserdata(State, Marshal.SizeOf<IntPtr>());
            Marshal.WriteIntPtr(ud, h);
            Push(FuncUDMeta);
            GetTableRaw(REGISTRYINDEX);
            SetMetatable(-2);
            Lua_pushcclosure(State, Marshal.GetFunctionPointerForDelegate(p), n + 1);
        }
        // lud, concat

        // metatable / udata
        [DllImport(LuaDll, EntryPoint = "lua_getmetatable", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_getmetatable(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_setmetatable", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_setmetatable(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_newuserdata", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_newuserdata(IntPtr l, int size);
        [DllImport(LuaDll, EntryPoint = "luaL_callmeta", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LuaL_callmeta(IntPtr l, int i, IntPtr ev);
        [DllImport(LuaDll, EntryPoint = "luaL_getmetafield", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LuaL_getmetafield(IntPtr l, int i, IntPtr ev);
        [DllImport(LuaDll, EntryPoint = "luaL_getmetatable", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LuaL_getmetatable(IntPtr l, IntPtr name);
        [DllImport(LuaDll, EntryPoint = "luaL_newmetatable", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LuaL_newmetatable(IntPtr l, IntPtr name);
        public override bool GetMetatable(int i)
        {
            CheckIndex(i);
            CheckStack(1);
            return Lua_getmetatable(State, i) != 0;
        }
        public override bool SetMetatable(int i)
        {
            CheckIndex(i);
            if (Top < 1)
                throw new LuaException("setmetatable nothing on the stack");
            return Lua_setmetatable(State, i) != 0;
        }
        public override string GetMetatableEventString(MetaEvent e)
        {
            switch (e)
            {
                case MetaEvent.Add:
                    return "__add";
                case MetaEvent.Subtract:
                    return "__sub";
                case MetaEvent.Multiply:
                    return "__mul";
                case MetaEvent.Divide:
                    return "__div";
                case MetaEvent.Pow:
                    return "__pow";
                case MetaEvent.UnaryMinus:
                    return "__unm";
                case MetaEvent.Concat:
                    return "__concat";
                case MetaEvent.Equals:
                    return "__eq";
                case MetaEvent.LessThan:
                    return "__lt";
                case MetaEvent.LessOrEquals:
                    return "__le";
                case MetaEvent.Index:
                    return "__index";
                case MetaEvent.NewIndex:
                    return "__newindex";
                case MetaEvent.Call:
                    return "__call";
                case MetaEvent.Finalizer:
                    return "__gc";
                case MetaEvent.WeakTable:
                    return "__mode";
                default:
                    return "";
            }
        }
        public override void Push(MetaEvent e)
        {
            Push(GetMetatableEventString(e));
        }
        public override bool CallMeta(int obj, string ev)
        {
            CheckIndex(obj);
            IntPtr b = Marshal.StringToHGlobalAnsi(ev);
            int r = LuaL_callmeta(State, obj, b);
            Marshal.FreeHGlobal(b);
            return r != 0;
        }
        public override bool CallMeta(int obj, MetaEvent ev)
        {
            return CallMeta(obj, GetMetatableEventString(ev));
        }
        public override bool GetMetaField(int obj, string ev)
        {
            CheckIndex(obj);
            IntPtr b = Marshal.StringToHGlobalAnsi(ev);
            int r = LuaL_getmetafield(State, obj, b);
            Marshal.FreeHGlobal(b);
            return r != 0;
        }
        public override bool GetMetaField(int obj, MetaEvent ev)
        {
            return GetMetaField(obj, GetMetatableEventString(ev));
        }
        public override void GetMetatableFromRegistry(string name)
        {
            IntPtr b = Marshal.StringToHGlobalAnsi(name);
            LuaL_getmetatable(State, b);
            Marshal.FreeHGlobal(b);
        }
        public override bool NewMetatable(string name)
        {
            IntPtr b = Marshal.StringToHGlobalAnsi(name);
            int r = LuaL_newmetatable(State, b);
            Marshal.FreeHGlobal(b);
            return r != 0;
        }

        // tableaccess
        [DllImport(LuaDll, EntryPoint = "lua_gettable", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_gettable(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_rawget", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_rawget(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_settable", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_settable(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_rawset", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_rawset(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_next", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_next(IntPtr l, int i);
        [DllImport(LuaDll, EntryPoint = "lua_rawgeti", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_rawgeti(IntPtr l, int i, int k);
        [DllImport(LuaDll, EntryPoint = "lua_rawseti", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_rawseti(IntPtr l, int i, int k);
        public override void GetTable(int i)
        {
            CheckIndex(i);
            if (Top < 1)
                throw new LuaException("no key on stack");
            Lua_gettable(State, i);
        }
        public override void GetTableRaw(int i)
        {
            CheckIndex(i);
            if (Top < 1)
                throw new LuaException("no key on stack");
            Lua_rawget(State, i);
        }
        public override void GetTableRaw(int i, int key)
        {
            CheckIndex(i);
            Lua_rawgeti(State, i, key);
        }
        public override void SetTable(int i)
        {
            CheckIndex(i);
            if (Top < 2)
                throw new LuaException("no key/value on stack");
            Lua_settable(State, i);
        }
        public override void SetTableRaw(int i)
        {
            CheckIndex(i);
            if (Top < 2)
                throw new LuaException("no key/value on stack");
            Lua_rawset(State, i);
        }
        public override void SetTableRaw(int i, int key)
        {
            CheckIndex(i);
            if (Top < 1)
                throw new LuaException("no value on stack");
            Lua_rawseti(State, i, key);
        }
        // iterate over table, key is at -2, value at -1, enumerable is type of value, only access them, dont change them
        public override IEnumerable<LuaType> Pairs(int i)
        {
            int t = Top + 1;
            i = ToAbsoluteIndex(i);
            CheckIndex(i);
            CheckStack(2);
            Push();
            while (Lua_next(State, i) != 0)
            {
                yield return Type(-1);
                Pop(1); // remove val, keep key for next
                if (Top != t)
                    throw new LuaException("pairs stack top mismatch");
            }
            // after traversal no key gets pushed
        }
        // iterate over a table in array stile, from t[1] up to the first nil found, enumerable is index/key, -1 is value, only access them, dont change them
        public override IEnumerable<int> IPairs(int i)
        {
            int t = Top;
            CheckIndex(i);
            CheckStack(1);
            int ind = 1;
            while (true)
            {
                Lua_rawgeti(State, i, ind);
                if (Lua_type(State, -1) == LuaType.Nil)
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
        [DllImport(LuaDll, EntryPoint = "lua_pcall", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaResult Lua_pcall(IntPtr l, int nargs, int nres, int errfunc);
        [DllImport(LuaDll, EntryPoint = "lua_call", CallingConvention = CallingConvention.Cdecl)]
        private static extern void Lua_call(IntPtr l, int nargs, int nres);
        private static readonly LuaCFunc PCallStackAttacher = (IntPtr p) =>
        {
            LuaState50 s = new LuaState50(p);
            string st = s.ToString(1);
            if (st == null)
                return 1;
            s.CurrentUpvalues = 1;
            int calldepth = (int)s.ToNumber(s.UPVALUEINDEX(1));
            int currdepth = s.GetCurrentFuncStackSize();
            s.Push($"{st}\r\n{s.GetStackTrace(1, currdepth - calldepth, "   at lua ")}");
            return 1;
        };
        public override void PCall(int nargs, int nres)
        {
            if (Top < nargs + 1)
                throw new LuaException($"pcall not enough vaues on the stack");
            int sd = GetCurrentFuncStackSize();
            Push(sd);
            Lua_pushcclosure(State, Marshal.GetFunctionPointerForDelegate(PCallStackAttacher), 1);
            int ecpos = ToAbsoluteIndex(-nargs - 2); // just under the func to be called
            Insert(ecpos);
            LuaResult r = Lua_pcall(State, nargs, nres, ecpos);
            Remove(ecpos);
            CheckError(r);
        }
        public override LuaResult PCall_Debug(int nargs, int nres, int errfunc)
        {

            if (Top < nargs + 1)
                throw new LuaException($"pcall not enough vaues on the stack");
            return Lua_pcall(State, nargs, nres, errfunc);
        }

        // debug
        [DllImport(LuaDll, EntryPoint = "lua_getstack", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_getstack(IntPtr l, int lvl, IntPtr ar);
        [DllImport(LuaDll, EntryPoint = "lua_getinfo", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_getinfo(IntPtr l, string what, IntPtr ar);
        public override string ToDebugString(int i)
        {
            switch (Type(i))
            {
                case LuaType.Nil:
                    return "nil";
                case LuaType.Boolean:
                    return ToBoolean(i) ? "true" : "false";
                case LuaType.LightUserData:
                    return $"<LightUserdata 0x{(uint)ToUserdata(i):X}>";
                case LuaType.Number:
                    return ToNumber(i).ToString();
                case LuaType.String:
                    return $"\"{ToString(i)}\"";
                case LuaType.Table:
                    return $"<table 0x{(uint)Lua_topointer(State, i):X}>";
                case LuaType.Function:
                    {
                        if (IsCFunction(i))
                        {
                            return $"<function, defined in C:0x{(uint)Lua_topointer(State, i):X}>";
                        }
                        int t = Top;
                        PushValue(i);
                        DebugInfo d = GetFuncInfo();
                        Top = t;
                        return $"<function {d.What} {d.NameWhat} {(d.Name ?? "null")} (defined in: {d.ShortSource}:{d.CurrentLine})>";
                    }
                case LuaType.UserData:
                    return $"<Userdata 0x{(uint)ToUserdata(i):X}>";
                case LuaType.Thread:
                    return $"<Thread 0x{(uint)Lua_topointer(State, i):X}>";
                default:
                    return "unknown";
            }
        }
        private DebugInfo ToDebugInfo(LuaDebugRecord r, IntPtr ar, bool free = true)
        {
            return new DebugInfo
            {
                Event = r.debugEvent,
                Name = Marshal.PtrToStringAnsi(r.name),
                NameWhat = Marshal.PtrToStringAnsi(r.namewhat) ?? "",
                What = Marshal.PtrToStringAnsi(r.what) ?? "",
                Source = Marshal.PtrToStringAnsi(r.source) ?? "",
                CurrentLine = r.currentline,
                NumUpvalues = r.nups,
                LineDefined = r.linedefined,
                ShortSource = r.short_src,
                ActivationRecord = ar,
                FreeAROnFinalize = free,
            };
        }
        public override DebugInfo GetStackInfo(int lvl, bool push = false)
        {
            IntPtr ar = Marshal.AllocHGlobal(Marshal.SizeOf<LuaDebugRecord>());
            if (Lua_getstack(State, lvl, ar) == 0)
            {
                Marshal.FreeHGlobal(ar);
                throw new LuaException("invalid call stack level");
            }
            if (Lua_getinfo(State, push ? "fulSn" : "ulSn", ar) == 0)
            {
                Marshal.FreeHGlobal(ar);
                throw new LuaException("somehow the option string got messed up");
            }
            LuaDebugRecord r = Marshal.PtrToStructure<LuaDebugRecord>(ar);
            return ToDebugInfo(r, ar);
        }
        public override DebugInfo GetFuncInfo()
        {
            CheckType(-1, LuaType.Function);
            IntPtr ar = Marshal.AllocHGlobal(Marshal.SizeOf<LuaDebugRecord>());
            if (Lua_getinfo(State, ">ulSn", ar) == 0)
            {
                Marshal.FreeHGlobal(ar);
                throw new LuaException("somehow the option string got messed up");
            }
            LuaDebugRecord r = Marshal.PtrToStructure<LuaDebugRecord>(ar);
            Marshal.FreeHGlobal(ar);
            return ToDebugInfo(r, IntPtr.Zero); // no ar here, cause we cannot change locals of this anyway
        }
        public override void PushDebugInfoFunc(DebugInfo i)
        {
            if (i.ActivationRecord == IntPtr.Zero)
                throw new LuaException("i has no ActivationRecord");
            if (Lua_getinfo(State, "f", i.ActivationRecord) == 0)
            {
                Marshal.FreeHGlobal(i.ActivationRecord);
                i.ActivationRecord = IntPtr.Zero;
                throw new LuaException("ActiationRecord seems to be invalid");
            }
        }

        private string GetFuncStackLevel(int lvl)
        {
            DebugInfo i = GetStackInfo(lvl);
            return $"{i.What} {i.NameWhat} {(i.Name ?? "null")} (defined in: {i.ShortSource}:{i.CurrentLine})";
        }
        public override int GetCurrentFuncStackSize()
        {
            int i = 0;
            IntPtr ar = Marshal.AllocHGlobal(Marshal.SizeOf<LuaDebugRecord>());
            while (true)
            {
                if (Lua_getstack(State, i, ar) == 0)
                {
                    Marshal.FreeHGlobal(ar);
                    return i;
                }
                i++;
            }
        }
        public override string GetStackTrace(int from = 0, int to = -1, string lineprefix = "")
        {
            string s = "";
            int l = from;
            while (l != to)
            {
                try
                {
                    string c = GetFuncStackLevel(l);
                    s += lineprefix + c + "\r\n";
                    l++;
                }
                catch (LuaException)
                {
                    break;
                }
            }
            return s;
        }
        [DllImport(LuaDll, EntryPoint = "lua_getlocal", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_getlocal(IntPtr L, IntPtr ar, int n);
        [DllImport(LuaDll, EntryPoint = "lua_setlocal", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_setlocal(IntPtr L, IntPtr ar, int n);
        [DllImport(LuaDll, EntryPoint = "lua_getupvalue", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_getupvalue(IntPtr L, int funcindex, int n);
        [DllImport(LuaDll, EntryPoint = "lua_setupvalue", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr Lua_setupvalue(IntPtr L, int funcindex, int n);
        [DllImport(LuaDll, EntryPoint = "lua_sethook", CallingConvention = CallingConvention.Cdecl)]
        private static extern int Lua_sethook(IntPtr L, IntPtr func, LuaHookMask mask, int count);

        // throws on invalid stack lvl, returns null on non existent local
        public override string GetLocalName(DebugInfo i, int localnum)
        {
            if (i.ActivationRecord == IntPtr.Zero)
                throw new LuaException("i has no ActivationRecord");
            IntPtr s = Lua_getlocal(State, i.ActivationRecord, localnum);
            Type(-1);
            if (s != IntPtr.Zero)
                Pop(1);
            return Marshal.PtrToStringAnsi(s);
        }
        public override void GetLocal(DebugInfo i, int localnum)
        {
            if (i.ActivationRecord == IntPtr.Zero)
                throw new LuaException("i has no ActivationRecord");
            IntPtr s = Lua_getlocal(State, i.ActivationRecord, localnum);
            if (s == IntPtr.Zero)
                throw new LuaException("invalid local");
        }
        public override void SetLocal(DebugInfo i, int localnum)
        {
            if (Top < 1)
                throw new LuaException("nothing on the stack to set to");
            if (i.ActivationRecord == IntPtr.Zero)
                throw new LuaException("i has no ActivationRecord");
            IntPtr s = Lua_setlocal(State, i.ActivationRecord, localnum);
            if (s == IntPtr.Zero)
                throw new LuaException("invalid local");
        }
        public override string GetUpvalueName(int funcidx, int upvalue)
        {
            CheckType(funcidx, LuaType.Function);
            IntPtr s = Lua_getupvalue(State, funcidx, upvalue);
            if (s != IntPtr.Zero)
                Pop(1);
            return Marshal.PtrToStringAnsi(s);
        }
        public override void GetUpvalue(int funcidx, int upvalue)
        {
            CheckType(funcidx, LuaType.Function);
            IntPtr s = Lua_getupvalue(State, funcidx, upvalue);
            if (s == IntPtr.Zero)
                throw new LuaException("invalid upvaule");
        }
        public override void SetUpvalue(int funcidx, int upvalue)
        {
            if (Top < 1)
                throw new LuaException("nothing on the stack to set to");
            CheckType(funcidx, LuaType.Function);
            IntPtr s = Lua_setupvalue(State, funcidx, upvalue);
            if (s == IntPtr.Zero)
                throw new LuaException("invalid upvaule");
        }
        public override string Where(int lvl)
        {
            try
            {
                DebugInfo i = GetStackInfo(lvl);
                if (i.CurrentLine > 0)
                    return $"{i.ShortSource}:{i.CurrentLine}";
            }
            catch (LuaException)
            {
            }
            return "";
        }
        private LuaHookFunc HookFunc = null;
        /// <summary>
        /// sets/unsets a lua hook. call with func = null or mask = LuaHookMask.None to remove.
        /// Note: gets autoatically removed when the LuaState object gets GC'd.
        /// catches all exceptions thrown, to not let then propagate through lua (which would break stuff).
        /// </summary>
        /// <param name="func"></param>
        /// <param name="mask"></param>
        /// <param name="count"></param>
        /// <exception cref="LuaException"></exception>
        public override void SetHook(Action<LuaState, DebugInfo> func, LuaHookMask mask, int count)
        {
            if (func == null || mask == LuaHookMask.None)
            {
                if (Lua_sethook(State, IntPtr.Zero, LuaHookMask.None, 0) != 1)
                    throw new LuaException("will never happen, always returns 1");
            }
            HookFunc = (IntPtr s, IntPtr debugrectord) =>
            {
                try
                {
                    LuaState st = new LuaState50(s);
                    int info = Lua_getinfo(s, "ulSn", debugrectord);
                    LuaDebugRecord r = Marshal.PtrToStructure<LuaDebugRecord>(debugrectord);
                    DebugInfo i;
                    if (info == 0)
                    {
                        i = new DebugInfo()
                        {
                            Event = r.debugEvent,
                        };
                    }
                    else
                    {
                        i = ToDebugInfo(r, debugrectord, false);
                    }
                    func(st, i);
                }
                catch (Exception e)
                {
                    Console.WriteLine("exception catched in lua hook:");
                    Console.WriteLine(e.ToString());
                }
            };
            if (Lua_sethook(State, Marshal.GetFunctionPointerForDelegate(HookFunc), mask, count) != 1)
                throw new LuaException("will never happen, always returns 1");
        }

        // checks
        public override void ArgError(int arg, string msg)
        {
            DebugInfo i = GetStackInfo(0);
            if (i.Name == null)
                i.Name = "?";
            if ("method".Equals(i.NameWhat))
            {
                arg--;
                if (arg == 0)
                    throw new LuaException($"calling `{i.Name}' on bad self ({msg}");
            }
            throw new LuaException($"bad argument #{arg} to `{i.Name}' ({msg})");
        }
        public override void TypeError(int i, string type)
        {
            ArgError(i, $"{type} expected, got {Type(i)}");
        }
        public override void TypeError(int i, LuaType t)
        {
            TypeError(i, t.ToString());
        }
        public override void CheckAny(int i)
        {
            if (Type(i) == LuaType.None)
                ArgError(i, "value expected");
        }
        public override double CheckNumber(int i)
        {
            double n = ToNumber(i);
            if (n == 0 && !IsNumber(i))
                TypeError(i, LuaType.Number);
            return n;
        }
        public override int CheckInt(int i)
        {
            return (int)CheckNumber(i);
        }
        public override string CheckString(int i)
        {
            string s = ToString(i);
            if (s == null)
                TypeError(i, LuaType.String);
            return s;
        }
        public override bool CheckBool(int i)
        {
            CheckType(i, LuaType.Boolean);
            return ToBoolean(i);
        }
        public override void CheckType(int i, params LuaType[] t)
        {
            LuaType ty = Type(i);
            if (!t.Contains(ty))
                throw new LuaException($"wrong type at {i}, expected {string.Join(",", t)}, found {ty}");
        }

        // load lua
        [DllImport(LuaDll, EntryPoint = "luaL_loadfile", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaResult LuaL_loadfile(IntPtr l, IntPtr t);
        [DllImport(LuaDll, EntryPoint = "luaL_loadbuffer", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaResult LuaL_loadbuffer(IntPtr l, IntPtr b, int bufflen, IntPtr name);
        [DllImport(LuaDll, EntryPoint = "lua_dofile", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaResult Lua_dofile(IntPtr l, IntPtr t);
        [DllImport(LuaDll, EntryPoint = "lua_dobuffer", CallingConvention = CallingConvention.Cdecl)]
        private static extern LuaResult Lua_dobuffer(IntPtr l, IntPtr b, int bufflen, IntPtr name);
        public override void LoadFile(string filename)
        {
            IntPtr f = Marshal.StringToHGlobalAnsi(filename);
            LuaResult r = LuaL_loadfile(State, f);
            Marshal.FreeHGlobal(f);
            CheckError(r);
        }
        public override void LoadBuffer(string code, string name)
        {
            IntPtr c = Marshal.StringToHGlobalAnsi(code);
            IntPtr n = Marshal.StringToHGlobalAnsi(name);
            LuaResult r = LuaL_loadbuffer(State, c, Encoding.ASCII.GetByteCount(code), n);
            Marshal.FreeHGlobal(c);
            Marshal.FreeHGlobal(n);
            CheckError(r);
        }
        public override void DoFile(string filename)
        {
            LoadFile(filename);
            PCall(0, MULTIRETURN);
        }
        public override void DoString(string code, string name = null)
        {
            if (name == null)
                name = code;
            LoadBuffer(code, name);
            PCall(0, MULTIRETURN);
        }
        public override int MULTIRETURN => -1;

        // opt
        public override double OptNumber(int idx, double def)
        {
            if (IsNoneOrNil(idx))
                return def;
            else
                return CheckNumber(idx);
        }
        public override int OptInt(int idx, int def)
        {
            if (IsNoneOrNil(idx))
                return def;
            else
                return CheckInt(idx);
        }
        public override string OptString(int idx, string def)
        {
            if (IsNoneOrNil(idx))
                return def;
            else
                return CheckString(idx);
        }
        public override bool OptBool(int idx, bool def)
        {
            if (IsNoneOrNil(idx))
                return def;
            else
                return CheckBool(idx);
        }

        // ref
        [DllImport(LuaDll, EntryPoint = "luaL_ref", CallingConvention = CallingConvention.Cdecl)]
        private static extern int LuaL_ref(IntPtr l, int t);
        [DllImport(LuaDll, EntryPoint = "luaL_unref", CallingConvention = CallingConvention.Cdecl)]
        private static extern void LuaL_unref(IntPtr l, int t, int re);

        public override Reference Ref()
        {
            if (Top < 1)
                throw new LuaException("ref nothing on the stack");
            return new Reference(LuaL_ref(State, REGISTRYINDEX));
        }
        public override void UnRef(Reference r)
        {
            LuaL_unref(State, REGISTRYINDEX, r.r);
        }
        public override void Push(Reference r)
        {
            Lua_rawgeti(State, REGISTRYINDEX, r.r);
        }
        public override Reference NOREF => new Reference(-2);
        public override Reference REFNIL => new Reference(-1);
    }
}
