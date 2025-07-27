using Microsoft.Extensions.ObjectPool;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shelltrac
{
    public readonly struct PooledObject<T> : IDisposable where T : class
    {
        private readonly T _value;
        private readonly ObjectPool<T> _pool;

        internal PooledObject(ObjectPool<T> pool)
        {
            _pool = pool;
            _value = _pool.Get();
        }

        public T Value => _value;

        public void Dispose()
        {
            _pool.Return(_value);
        }
    }

    public static class ShelltracPools
    {
        private static readonly ObjectPool<List<Expr>> _expressionLists =
            new DefaultObjectPool<List<Expr>>(new ListPooledObjectPolicy<Expr>());

        private static readonly ObjectPool<List<Stmt>> _statementLists =
            new DefaultObjectPool<List<Stmt>>(new ListPooledObjectPolicy<Stmt>());
        
        private static readonly ObjectPool<List<Token>> _tokenLists =
            new DefaultObjectPool<List<Token>>(new ListPooledObjectPolicy<Token>());

        private static readonly ObjectPool<List<string>> _stringLists =
            new DefaultObjectPool<List<string>>(new ListPooledObjectPolicy<string>());

        private static readonly ObjectPool<List<object?>> _objectLists =
            new DefaultObjectPool<List<object?>>(new ListPooledObjectPolicy<object?>());

        private static readonly ObjectPool<List<Dictionary<string, string>>> _listDictStringString =
            new DefaultObjectPool<List<Dictionary<string, string>>>(new ListPooledObjectPolicy<Dictionary<string, string>>());

        private static readonly ObjectPool<StringBuilder> _stringBuilders =
            new DefaultObjectPool<StringBuilder>(new StringBuilderPooledObjectPolicy());
        
        private static readonly ObjectPool<Dictionary<string, Expr>> _dictionaryStringExpr =
            new DefaultObjectPool<Dictionary<string, Expr>>(new DictionaryPooledObjectPolicy<string, Expr>());

        private static readonly ObjectPool<Dictionary<string, object?>> _dictionaryStringObject =
            new DefaultObjectPool<Dictionary<string, object?>>(new DictionaryPooledObjectPolicy<string, object?>());

        private static readonly ObjectPool<Dictionary<string, string>> _dictionaryStringString =
            new DefaultObjectPool<Dictionary<string, string>>(new DictionaryPooledObjectPolicy<string, string>());

        public static PooledObject<List<Expr>> GetExpressionList() => new(_expressionLists);
        public static PooledObject<List<Stmt>> GetStatementList() => new(_statementLists);
        public static PooledObject<List<Token>> GetTokenList() => new(_tokenLists);
        public static PooledObject<List<string>> GetStringList() => new(_stringLists);
        public static PooledObject<List<object?>> GetObjectList() => new(_objectLists);
        public static PooledObject<List<Dictionary<string, string>>> GetListDictStringString() => new(_listDictStringString);
        public static PooledObject<StringBuilder> GetStringBuilder() => new(_stringBuilders);
        public static PooledObject<Dictionary<string, Expr>> GetDictionaryStringExpr() => new(_dictionaryStringExpr);
        public static PooledObject<Dictionary<string, object?>> GetDictionaryStringObject() => new(_dictionaryStringObject);
        public static PooledObject<Dictionary<string, string>> GetDictionaryStringString() => new(_dictionaryStringString);
    }

    public class ListPooledObjectPolicy<T> : IPooledObjectPolicy<List<T>>
    {
        public List<T> Create() => new();

        public bool Return(List<T> obj)
        {
            obj.Clear();
            return true;
        }
    }

    public class DictionaryPooledObjectPolicy<TKey, TValue> : IPooledObjectPolicy<Dictionary<TKey, TValue>> where TKey : notnull
    {
        public Dictionary<TKey, TValue> Create() => new();

        public bool Return(Dictionary<TKey, TValue> obj)
        {
            obj.Clear();
            return true;
        }
    }

    public class StringBuilderPooledObjectPolicy : IPooledObjectPolicy<StringBuilder>
    {
        public StringBuilder Create() => new();

        public bool Return(StringBuilder obj)
        {
            obj.Clear();
            if (obj.Capacity > 4096)
            {
                obj.Capacity = 4096;
            }
            return true;
        }
    }
}
