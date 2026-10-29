# lmscript tests and releases

Root test.lms drives .NET builds, package-free unit tests, both conformance catalogs and the annotated Score example. RUN_NATIVE=1 plus RUNTIME_ID also builds/runs NativeAOT. Install current npm @lmctl-ai/lmctl. CI tests five platforms on main pushes, PRs and manual dispatch; successful main runs publish three native executables per platform with licenses/checksums.
