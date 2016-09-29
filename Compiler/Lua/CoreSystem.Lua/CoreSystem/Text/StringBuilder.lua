local System = System
local throw = System.throw
local Collection = System.Collection
local addCount = Collection.addCount
local removeArrayAll = Collection.removeArrayAll
local clearCount = Collection.clearCount
local ArgumentNullException = System.ArgumentNullException

local table = table
local tinsert = table.insert
local tconcat = table.concat

local StringBuilder = {}

local function build(this, value, startIndex, length)
    value = System.String.substring(value, startIndex, length)
    local len = #value
    if len > 0 then
        tinser(this, value)
        addCount(this, len) 
    end
end

function StringBuilder.__ctor__(this, ...)
    local len = select("#", ...)
    if len == 0 then
    elseif len == 1 or len == 2 then
        local value = ...
        if type(value) == "string" then
            build(this, value, 0, #value)
        else
            build(this, "", 0, 0)
        end
    else 
        local value, startIndex, length = ...
        build(this, value, startIndex, length)
    end
end

StringBuilder.getLength = Collection.getCount

function StringBuilder.Append(this, ...)
    local len = select("#", "...")
    if len == 1 then
        local value = ...
        if value ~= nil then
            value = value:ToString()
            tinsert(this, value)
            addCount(this, #value) 
        end
    else
        local value, startIndex, length = ...
        if value == nil then
            throw(ArgumentNullException("value"))
        end
        value = value:Substring(startIndex, length)
        tinsert(this, value)
        addCount(this, #value) 
    end
    return this
end

function StringBuilder.AppendFormat(this, format, ...)
    local value = format:Format(...)
    tinsert(this, this)
    addCount(this, #value) 
    return this
end

function StringBuilder.AppendLine(this, value)
    local count = 1;
    if value ~= nil then
        tinsert(this, value)
        count = count + #value
    end
    tinsert(this, "\n")
    addCount(this, count) 
    return this
end

function StringBuilder.Clear(this)
    removeArrayAll(this)
    clearCount(this)
    return this
end

StringBuilder.ToString = tconcat
StringBuilder.__tostring = StringBuilder.ToString
StringBuilder.__len = StringBuilder.GetLength

System.define("System.StringBuilder", StringBuilder)
 