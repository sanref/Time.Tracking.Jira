# Changelog

Un archivo por version publicada, nombrado `<version>.md` (por ejemplo `4.0.1.md`).
El listado de la carpeta hace de indice: no hay que mantener un indice aparte.

La version de cada entrada es la misma que reporta el ejecutable en
`AssemblyFileVersion`, que sale de `next-version` en [GitVersion.yml](../GitVersion.yml)
mientras no exista un tag git mayor.

Cada archivo describe **que cambia para quien usa la aplicacion**. Los detalles de
implementacion solo entran cuando explican un cambio de comportamiento visible.
