
function main()
    print("running main");
    while true do
        -- print("running a frame");
        emu.yield() -- frameadvance() also works
        local x = unityhawk.callmethod("DoSomething", "hello from lua");
        print(x);
    end
end

main()