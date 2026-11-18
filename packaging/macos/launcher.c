#include <limits.h>
#include <mach-o/dyld.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char *argv[])
{
    (void)argc;
    char executable[PATH_MAX];
    char resolved[PATH_MAX];
    uint32_t size = sizeof(executable);
    if (_NSGetExecutablePath(executable, &size) != 0 || !realpath(executable, resolved)) {
        fputs("PhoneBox: could not resolve the launcher path\n", stderr);
        return 1;
    }
    *strrchr(resolved, '/') = '\0'; // realpath returned an absolute path.

#if defined(__arm64__)
    const char *rid = "osx-arm64";
#else
    const char *rid = "osx-x64";
#endif
    char agent[PATH_MAX];
    int length = snprintf(agent, sizeof(agent), "%s/../Resources/%s/PhoneBoxAgent", resolved, rid);
    if (length < 0 || (size_t)length >= sizeof(agent)) {
        fputs("PhoneBox: the agent path is too long\n", stderr);
        return 1;
    }
    argv[0] = agent;
    execv(agent, argv);
    perror("PhoneBox: could not start the agent");
    return 1;
}
