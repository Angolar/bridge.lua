local System = System
local throw = System.throw
local each = System.each
local identityFn = System.identityFn
local Collection = System.Collection
local wrap = Collection.wrap
local unWrap = Collection.unWrap
local NullReferenceException = System.NullReferenceException
local ArgumentNullException = System.ArgumentNullException
local ArgumentOutOfRangeException = System.ArgumentOutOfRangeException
local InvalidOperationException = System.InvalidOperationException
local EqualityComparer_1 = System.EqualityComparer_1
local Comparer_1 = System.Comparer_1
local Empty = System.Array.Empty

local select = select
local tinser = table.insert
local getmetatable = getmetatable

local InternalEnumerable = System.define("System.Linq.InternalEnumerable", {
    __inherits__ = { System.IEnumerable }
})

local function createInternal(T, GetEnumerator)
    assert(T)
    return setmetatable({ __genericT__ = T, GetEnumerator = GetEnumerator }, InternalEnumerable)
end

local function create(source, GetEnumerator)
    return createInternal(source.__genericT__, GetEnumerator)
end 

local InternalEnumerator = System.define("System.Linq.InternalEnumerator", {
    __inherits__ = { System.IEnumerator }
})

local function IEnumerator(source, tryGetNext, init)
    local state = 1
    local current
    local en
    return setmetatable({
        MoveNext = function()
            if state == 1 then
                state = 2
                if source then
                    en = source:GetEnumerator() 
                end
                if init then
                   init(en) 
                end
            end
            if state == 2 then
                local ok, v = tryGetNext(en)
                if ok then
                    current = v
                    return true
                elseif en then
                    local dispose = en.Dispose
                    if dispose then
                        dispose(en)
                    end    
                end
            end
            return false
        end,
        getCurrent = function()
            return current
        end
    }, InternalEnumerator)
end

local Enumerable = {}
System.define("System.Linq.Enumerable", Enumerable)

function Enumerable.Where(source, predicate)
    if source == nil then throw(ArgumentNullException("source")) end
    if predicate == nil then throw(ArgumentNullException("predicate")) end
    return create(source, function() 
        local index = -1
        return IEnumerator(source, function(en)
            while en:MoveNext() do
                local current = en:getCurrent()
                index = index + 1
                if predicate(current, index) then
                    return true, current
                end
            end 
            return false
        end)
    end)
end

function Enumerable.Select(source, selector, T)
    if source == nil then throw(ArgumentNullException("source")) end
    if selector == nil then throw(ArgumentNullException("selector")) end
    return createInternal(T, function()
        local index = -1
        return IEnumerator(source, function(en) 
            if en:MoveNext() then
                index = index + 1
                return true, selector(en:getCurrent(), index)
            end
            return false
        end)
    end)
end

local function selectMany(source, collectionSelector, resultSelector, T)
    if source == nil then throw(ArgumentNullException("source")) end
    if collectionSelector == nil then throw(ArgumentNullException("collectionSelector")) end
    if resultSelector == nil then throw(ArgumentNullException("resultSelector")) end
    return createInternal(T, function() 
        local midEn
        local index = -1
        return IEnumerator(source, function(en) 
            while true do
                if midEn and midEn:MoveNext() then
                    if resultSelector ~= nil then
                        return true, resultSelector(midEn, midEn:getCurrent())
                    end
                    return true, midEn:getCurrent()
                else
                    if not en:MoveNext() then return false end
                    index = index + 1
                    midEn = collectionSelector(en:getCurrent(), index):GetEnumerator()
                    if midEn == nil then
                        throw(NullReferenceException())
                    end
                end  
            end
        end)
    end)
end

function Enumerable.SelectMany(source, ...)
    local function identityFn(s, x)
        return x
    end

    local len = select("#", ...)
    if len == 2 then
        local collectionSelector, T = ...
        return selectMany(source, collectionSelector, identityFn, T)
    else
        return selectMany(source, ...)
    end
end

function Enumerable.Take(source, count)
    if source == nil then throw(ArgumentNullException("source")) end
    return create(source, function()
        return IEnumerator(source, function(en)
            if count > 0 then
                if en:MoveNext() then
                    count = count - 1
                    return true, en:getCurrent()
                end
            end
            return false
        end)
    end)
end

function Enumerable.TakeWhile(source, predicate)
    if source == nil then throw(ArgumentNullException("source")) end
    if predicate == nil then throw(ArgumentNullException("predicate")) end
    return create(source, function()
        local index = -1
        return IEnumerator(source, function(en)
            if en:MoveNext() then
                local current = en:getCurrent()
                index = index + 1
                if not predicate(current, index) then
                    return false
                end
                return true, current
            end
            return false
        end)
    end)
end

function Enumerable.Skip(source, count)
    if source == nil then throw(ArgumentNullException("source")) end
    return create(source, function()
        return IEnumerator(source, function(en)
            while count > 0 and en:MoveNext() do count = count - 1 end
            if count <= 0 then
                if en:MoveNext() then
                    return true, en:getCurrent() 
                end
            end
            return false
        end)
    end)
end

function Enumerable.SkipWhile(source, predicate)
    if source == nil then throw(ArgumentNullException("source")) end
    if predicate == nil then throw(ArgumentNullException("predicate")) end
    return create(source, function()
        local index = -1
        local isSkipEnd = false
        return IEnumerator(source, function(en)
            while not isSkipEnd do
                if en:MoveNext() then
                    local current = en:getCurrent()
                    index = index + 1
                    if not predicate(current, index) then
                        isSkipEnd = true
                        return true, current
                    end     
                else 
                    return false
                end
            end
            if en:MoveNext() then
                return true, en:getCurrent()
            end
            return false
        end)
    end)
end

local Lookup = {}
function Lookup.__ctor__(this, comparer)
    this.comparer = comparer or EqualityComparer_1(this.__genericTKey__).getDefault()
    this.groups = {}
    this.count = 0
end

function Lookup.get(this, key)
    local hashCode = this.comparer.GetHashCode(key)
    local grouping = this.groups[hashCode]
    if grouping ~= nil then return grouping end 
    return Empty(this.__genericTElement__)
end

function Lookup.GetCount(this)
    return this.count
end

function Lookup.Contains(this, key)
    local hashCode = this.comparer.GetHashCode(key)
    return this.groups[hashCode] ~= nil
end

function Lookup.GetEnumerator(this)
    return Collection.dictionaryEnumerator(this.groups, 2)
end

local LookupFn = System.define("System.Linq.Lookup", function(TKey, TElement)
    local cls = {
        __genericTKey__ = TKey,
        __genericTElement__ = TElement,
    }
    return cls
end, Lookup)

local IGrouping = System.defInf("System.Linq.IGrouping")

local Grouping = {}
Grouping.GetEnumerator = Collection.arrayEnumerator 

function Grouping.getKey(this)
    return this.key
end

function Grouping.getCount(this)
    return #this
end

Grouping.__inherits__ = { IGrouping }
System.define("System.Linq.Grouping", Grouping)

local function addToLookup(this, key, value)
    local hashCode = this.comparer.GetHashCode(key)
    local group = this.groups[hashCode]
    if group == nil then
        group = setmetatable({ key = key, __genericT__ = this.__genericTElement__ }, Grouping)
        this.groups[hashCode] = group        
        this.count = this.count + 1
    end
    tinser(group, wrap(value))
end

local function createLookup(source, keySelector, elementSelector, comparer, TKey, TElement)
    local lookup = LookupFn(TKey, TElement)(comparer)
    for _, item in each(source) do
        addToLookup(lookup, keySelector(item), elementSelector(item))
    end
    return lookup
end

local function groupBy(source, keySelector, elementSelector, comparer, TKey, TElement)
    if source == nil then throw(ArgumentNullException("source")) end
    if keySelector == nil then throw(ArgumentNullException("keySelector")) end
    if elementSelector == nil then throw(ArgumentNullException("elementSelector")) end
    return createInternal(IGrouping, function()
        return createLookup(source, keySelector, elementSelector, comparer, TKey, TElement):GetEnumerator()
    end)
end

function Enumerable.GroupBy(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 2 then
         local keySelector, TKey = ...
         return groupBy(source, keySelector, identityFn, nil, TKey, source.__genericT__)
    elseif len == 3 then
         local keySelector, comparer, TKey = ...
         return groupBy(source, keySelector, identityFn, comparer, TKey, source.__genericT__)
    elseif len == 4 then
         local keySelector, elementSelector, TKey, TElement = ...
         return groupBy(source, keySelector, elementSelector, nil, TKey, TElement)
    else
        return groupBy(source, ...)
    end
end

local function groupBySelect(source, keySelector, elementSelector, resultSelector, comparer, TKey, TElement, TResult)
    if source == nil then throw(ArgumentNullException("source")) end
    if keySelector == nil then throw(ArgumentNullException("keySelector")) end
    if elementSelector == nil then throw(ArgumentNullException("elementSelector")) end
    if resultSelector == nil then throw(ArgumentNullException("resultSelector")) end
    return createInternal(TResult, function()
        local lookup = createLookup(source, keySelector, elementSelector, comparer, TKey, TElement)
        return IEnumerator(lookup, function(en)
            if en:MoveNext() then
                local current = en:getCurrent()
                return resultSelector(current.key, current)
            end
            return false
        end, TResult)
    end)
end

function Enumerable.GroupBySelect(source, ...)
    local len = select("#", ...)
    if len == 4 then
        local keySelector, resultSelector, TKey, TResult = ...
        return groupBySelect(source, keySelector, identityFn, resultSelector, nil, TKey, source.__genericT__, TResult)
    elseif len == 5 then
        local keySelector, resultSelector, comparer, TKey, TResult = ...
        return groupBySelect(source, keySelector, identityFn, resultSelector, comparer, TKey, source.__genericT__, TResult)
    elseif len == 6 then
        local keySelector, elementSelector, resultSelector, TKey, TElement, TResult = ...
        return groupBySelect(source, keySelector, elementSelector, resultSelector, nil, TKey, TElement, TResult)
    else
        return groupBySelect(source, ...)
    end
end

function Enumerable.Concat(first, second)
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    return create(first, function()
        local secondEn
        return IEnumerator(first, function(en)
            if secondEn == nil then
                if en:MoveNext() then
                    return true, en:getCurrent()
                end
                secondEn = second:GetEnumerator()
            end
            if secondEn:MoveNext() then
                return true, secondEn:getCurrent()
            end
            return false
        end)
    end)
end

function Enumerable.Zip(first, second, resultSelector, TResult) 
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    if resultSelector == nil then throw(ArgumentNullException("resultSelector")) end
    return createInternal(TResult, function()
        local e2
        return IEnumerator(first, function(e1)
            if e1:MoveNext() and e2:MoveNext() then
                return true, resultSelector(e1:getCurrent(), e2:getCurrent())
            end
        end, 
        function()
            e2 = second:GetEnumerator()
        end)
    end)
end

local function addToSet(set, v, getHashCode)
    local hashCode = getHashCode(v)
    if set[hashCode] == nil then
        set[hashCode] = true
        return true
    end
    return false
end

local function removeFromSet(set, v, getHashCode)
    local hashCode = getHashCode(v)
    if set[hashCode] ~= nil then
        set[hashCode] = nil
        return true
    end
    return false
end

local function getComparer(source, comparer)
    return comparer or EqualityComparer_1(source.__genericT__).getDefault()
end

function Enumerable.Distinct(source, comparer)
    if source == nil then throw(ArgumentNullException("source")) end
    return create(source, function()
        local set = {}
        local getHashCode = getComparer(source, comparer).GetHashCode
        return IEnumerator(source, function(en)
            while en:MoveNext() do
                local current = en:getCurrent()
                if addToSet(set, current, getHashCode) then
                    return true, current  
                end
            end
            return false
        end)
    end)
end

function Enumerable.Union(first, second, comparer)
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    return create(first, function()
        local set = {}
        local getHashCode = getComparer(first, comparer).GetHashCode
        local secondEn
        return IEnumerator(first, function(en)
            if secondEn == nil then
                while en:MoveNext() do
                    local current = en:getCurrent()
                    if addToSet(set, current, getHashCode) then
                        return true, current  
                    end
                end
                secondEn = second:GetEnumerator()
            end
            while secondEn:MoveNext() do
                local current = secondEn:getCurrent()
                if addToSet(set, current, getHashCode) then
                    return true, current  
                end
            end
            return false
        end)
    end)
end

function Enumerable.Intersect(first, second, comparer)
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    return create(source, function()
        local set = {}
        local getHashCode = getComparer(first, comparer).GetHashCode
        return IEnumerator(first, function(en)
            while en:MoveNext() do
                 local current = en:getCurrent()
                 if removeFromSet(set, current, getHashCode) then
                    return true, current
                 end
            end
            return false
        end,
        function()
            for _, v in each(second) do
                addToSet(set, v, getHashCode)
            end
        end)
    end) 
end

function Enumerable.Except(first, second, comparer)
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    return create(first, function()
        local set = {}
        local getHashCode = getComparer(first, comparer).GetHashCode
        return IEnumerator(first, function(en) 
            while en:MoveNext() do
                local current = en:getCurrent()
                if addToSet(set, current, getHashCode) then
                    return true, current  
                end
            end
            return false
        end,
        function()
            for _, v in each(second) do
                addToSet(set, v, getHashCode)
            end
        end)
    end)
end

function Enumerable.Reverse(source)
    if source == nil then throw(ArgumentNullException("source")) end
    return create(source, function()
        local t = {}    
        local index
        return IEnumerator(nil, function() 
            if index > 1 then
                index = index - 1
                return true, unWrap(t[index])
            end
            return false
        end, 
        function() 
            for _, v in each(source) do
                tinser(t, wrap(v))
            end  
            index = #t + 1
        end)
    end)
end

function Enumerable.SequenceEqual(first, second, comparer)
    if first == nil then throw(ArgumentNullException("first")) end
    if second == nil then throw(ArgumentNullException("second")) end
    local equals = getComparer(first, comparer).Equals
    local e1 = first:GetEnumerator()
    local e2 = second:GetEnumerator()
    while e1:MoveNext() do
        if not(e2:MoveNext() and equals(e1:getCurrent(), e2:getCurrent())) then
            return false
        end
    end
    if e2:MoveNext() then
        return false
    end
    return true
end

Enumerable.ToArray = Collection.toArray

function Enumerable.ToList(source)
    return System.List(source.__genericT__)(source)
end

local function toDictionary(source, keySelector, elementSelector, comparer, TKey, TValue)
    if source == nil then throw(ArgumentNullException("source")) end
    if keySelector == nil then throw(ArgumentNullException("keySelector")) end
    if elementSelector == nil then throw(ArgumentNullException("elementSelector")) end
    local dict = System.Dictionary(TKey, TValue)(comparer)
    for _, v in each(source) do
        dict:Add(keySelector(v), elementSelector(v))
    end
    return dict
end

function Enumerable.ToDictionary(source, ...)
    local len = select("#", ...)
    if len == 2 then
        local keySelector, TKey = ...
        return toDictionary(source, keySelector, identityFn, nil, TKey, source.__genericT__)
    elseif len == 3 then
        local keySelector, comparer, TKey = ...
        return toDictionary(source, keySelector, identityFn, comparer, TKey, source.__genericT__)
    elseif len == 4 then
        local keySelector, elementSelector, TKey, TElement = ...
        return toDictionary(source, keySelector, elementSelector, nil, TKey, TElement)
    else
        return toDictionary(source, ...)
    end
end

local function toLookup(source, keySelector, elementSelector, comparer, TKey, TElement )
    if source == nil then throw(ArgumentNullException("source")) end
    if keySelector == nil then throw(ArgumentNullException("keySelector")) end
    if elementSelector == nil then throw(ArgumentNullException("elementSelector")) end
    return createLookup(source, keySelector, elementSelector, comparer, TKey, TElement)
end

function Enumerable.ToLookup(source, ...)
    local len = select("#", ...)
    if len == 2 then
        local keySelector, TKey = ...
        return toLookup(source, keySelector, identityFn, nil, TKey, source.__genericT__)
    elseif len == 3 then
        local keySelector, comparer, TKey = ...
        return toLookup(source, keySelector, identityFn, comparer, TKey, source.__genericT__)
    elseif len == 4 then
        local keySelector, elementSelector, TKey, TElement = ...
        return toLookup(source, keySelector, elementSelector, nil, TKey, TElement)
    else
        return toLookup(source, ...)
    end
end

local function first(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then  
        local en = source:GetEnumerator()
        if en:MoveNext() then 
            return true, en:getCurrent()
        end
        return false, 0
    else
        local predicate = ...
        if predicate == nil then throw(ArgumentNullException("predicate")) end
        for _, v in each(source) do
            if predicate(v) then 
                return true, v
            end
        end
        return false, 1
    end
end

function Enumerable.First(source, ...)
    local ok, result = first(source, ...)
    if ok then return result end
    if result == 0 then
        throw(InvalidOperationException("NoElements"))
    end
    throw(InvalidOperationException("NoMatch"))
end

function Enumerable.FirstOrDefault(source, ...)
    local ok, result = first(source, ...)
    return ok and result or source.__genericT__.__default__()
end

local function last(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then
        local en = source:GetEnumerator()
        if en:MoveNext() then 
            local result
            repeat
                result = en:getCurrent()
            until not each.MoveNext()
            return true, result
        end
        return false, 0
    else
        local predicate = ...
        if predicate == nil then throw(ArgumentNullException("predicate")) end
        local result, found
        for _, v in each(source) do
            if predicate(v) then
                result = v
                found = true
            end
        end    
        if found then return true, result end
        return false, 1
    end
end

function Enumerable.Last(source, ...)
    local ok, result = last(source, ...)
    if ok then return result end
    if result == 0 then
        throw(InvalidOperationException("NoElements"))
    end
    throw(InvalidOperationException("NoMatch"))
end

function Enumerable.LastOrDefault(source, ...)
    local ok, result = last(source, ...)
    return ok and result or source.__genericT__.__default__()
end

local function single(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then
        local en = source:GetEnumerator()
        if not en:MoveNext() then return false, 0 end
        local result = en:getCurrent()
        if not en:MoveNext() then
            return true, result
        end
        return false, 1
    else
        local predicate = ...
        if predicate == nil then throw(ArgumentNullException("predicate")) end
        local result, found
        for _, v in each(source) do
            if predicate(v) then
                result = v
                if found then
                    return false, 1
                end
                found = true
            end
        end
        if foun then return true, result end    
        return false, 0    
    end
end

function Enumerable.Single(source, ...)
    local ok, result = single(source, ...)
    if ok then return result end
    if result == 0 then
        throw(InvalidOperationException("NoElements"))
    end
    throw(InvalidOperationException("MoreThanOneMatch"))
end

function Enumerable.SingleOrDefault(source, ...)
    local ok, result = single(source, ...)
    return ok and result or source.__genericT__.__default__()
end

local function elementAt(source, index)
    if source == nil then throw(ArgumentNullException("source")) end
    if index < 0 then return false end
    local en = source:GetEnumerator()
    while true do
        if not en:MoveNext() then return false end
        if index == 0 then return true, en:getCurrent() end
        index = index - 1
    end
end

function Enumerable.ElementAt(source, index)
    local ok, result = elementAt(source, index)
    if ok then return result end
    throw(ArgumentOutOfRangeException("index"))
end

function Enumerable.ElementAtOrDefault(source, index)
    local ok, result = elementAt(source, index)
    return ok and result or source.__genericT__.__default__()
end

function Enumerable.Range(start, count)
    if count < 0 then throw(ArgumentOutOfRangeException("count")) end
    return createInternal(System.Int, function()
        local index = -1
        return IEnumerator(nil, function()
            index = index + 1
            if index < count then
                return true, start + index  
            end
            return false
        end)
    end)
end

function Enumerable.Repeat(element, count, T)
    if count < 0 then throw(ArgumentOutOfRangeException("count")) end
    return createInternal(T, function()
        local index = -1
        return IEnumerator(nil, function()
            index = index + 1
            if index < count then
                return true, element  
            end
            return false
        end)
    end)
end

function Enumerable.Any(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then
        local en = source:GetEnumerator()
        return en:MoveNext()
    else
        local predicate = ...
        if predicate == nil then throw(ArgumentNullException("predicate")) end
        for _, v in each(source) do
            if predicate(v) then
                return true
            end
        end
        return false
    end
end

function Enumerable.All(source, predicate)
    if source == nil then throw(ArgumentNullException("source")) end
    if predicate == nil then throw(ArgumentNullException("predicate")) end
    for _, v in each(source) do
        if not predicate(v) then
            return false
        end
    end
    return true
end

function Enumerable.Count(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then
        local count = 0
        local en = source:GetEnumerator()
        while en:MoveNext() do 
            count = count + 1 
        end
        return count
    else
        local predicate = ...
        if predicate == nil then throw(ArgumentNullException("predicate")) end
        local count = 0
        for _, v in each(source) do
            if predicate(v) then
                count = count + 1
            end
        end
        return count
    end
end

function Enumerable.Contains(source, value, comparer)
    if source == nil then throw(ArgumentNullException("source")) end
    local equals = getComparer(source, comparer).Equals
    for _, v in each(source) do
        if equals(v, value) then
            return true
        end
    end
    return false
end

function Enumerable.Aggregate(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...);
    if len == 1 then
        local func = ...
        if func == nil then throw(ArgumentNullException("func")) end
        local e = source:GetEnumerator()
        if not e:MoveNext() then throw(InvalidOperationException("NoElements")) end
        local result = e:getCurrent()
        while e:MoveNext() do
            result = func(result, e:getCurrent())
        end
        return result
    elseif len == 2 then
        local seed, func = ...
        if func == nil then throw(ArgumentNullException("func")) end
        local result = seed
        for _, element in each(source) do
            result = func(result, element)
        end
        return result
    else 
        local seed, func, resultSelector = ...;
        if func == nil then throw(ArgumentNullException("func")) end
        if resultSelector == nil then throw(ArgumentNullException("resultSelector")) end
        local result = seed
        for _, element in each(source) do
            result = func(result, element)
        end
        return resultSelector(result)
    end
end

function Enumerable.Sum(source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    if len == 0 then
        local sum = 0
        for _, v in each(source) do
            sum = sum + v
        end
        return sum
    else
        local selector = ...
        if selector == nil then throw(ArgumentNullException("selector")) end
        local sum = 0
        for _, v in each(source) do
            sum = sum + selector(v)
        end
        return sum
    end
end

local function minOrMax(compareFn, source, ...)
    if source == nil then throw(ArgumentNullException("source")) end
    local len = select("#", ...)
    local selector, T 
    if len == 0 then
        selector, T = identityFn, source.__genericT__
    else
        selector, T = ...
        if selector == nil then throw(ArgumentNullException("selector")) end
    end
    local compare = Comparer_1(T).getDefault().Compare
    local value = T.__default__()
    if value == nil then
        for _, x in each(source) do
            x = selector(x)
            if x ~= nil and (value == nil or compareFn(compare, x, value)) then
                value = x
            end 
        end
        return value
    else
        local hasValue = false
        for _, x in each(source) do
            x = selector(x)
            if hasValue then
                if compareFn(compare, x, value) then
                    value = x
                end
            else
                value = x
                hasValue = true
            end
        end
        if hasValue then return value end
        throw(InvalidOperationException("NoElements"))
    end
end

function Enumerable.Min(source, ...)
    local function minFn(compare, x, y)
        return compare(x, y) < 0
    end
    return minOrMax(minFn, source, ...)
end

function Enumerable.Max(source, ...)
    local function maxFn(compare, x, y)
        return compare(x, y) > 0
    end
    return minOrMax(maxFn, source, ...)
end




