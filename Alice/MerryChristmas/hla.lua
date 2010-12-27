function Init()
    indicator:name("High Low Average");
    indicator:description("High/Low Average");
    indicator:requiredSource(core.Bar);
    indicator:type(core.Oscillator);

    indicator.parameters:addInteger("N", "Number of periods for HLA", "", 20, 2, 300);
    indicator.parameters:addInteger("M", "Multiply By", "", 2, 1, 3);
    indicator.parameters:addColor("clrMva", "Color of HLA", "", core.rgb(255, 0, 0));
end

local first = 0;        -- first period we can calculate
local n = 0;            -- MVA parameter
local m = 0;            -- MVA parameter
local source = nil;     -- source
local mva = nil;        -- moving average
local hl = nil;
-- initializes the instance of the indicator
function Prepare()
    source = instance.source;
    n = instance.parameters.N;
    m = instance.parameters.M;

    local name = profile:id() .. "(" .. source:name() .. "," .. n .. "," .. m .. ")";
    instance:name(name);

    mva = instance:addStream("MSD", core.Line, name .. ".MSD", "MSD", instance.parameters.clrMva,  first)
end

-- calculate the value
function Update(period)
	local v = source.high[period] - source.low[period];	
    if (period == 0) then
			hl = v;
		else
			hl = mva[period-1]/m + (v - mva[period-1]/m) / (n + 1);
    end
		mva[period] = hl * m;
end

