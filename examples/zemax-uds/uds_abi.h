/* Copyright (c) 2026 Javier Ruiz
 * SPDX-License-Identifier: MIT */

/* uds_abi.h — the two structures OpticStudio passes to a user-defined surface DLL, as this example
 * uses them.
 *
 * THE INTERFACE IS OPTICSTUDIO'S. The function name UserDefinedSurface5, the two structures, their
 * field names and their layout are defined by Ansys, in the header usersurf.h that comes with
 * OpticStudio (in its DLL\Surfaces folder), and described in OpticStudio's documentation of the
 * "User Defined" surface. The function receives a USER_DATA (the ray: position, direction, normal,
 * path) and a FIXED_DATA5 (the surface: what is being asked for, the wavelength, the indices either
 * side, the parameters, and the ray's electric field for polarization ray tracing).
 *
 * This header declares that layout, with the names the interface requires, so that the example
 * builds without OpticStudio installed. Its comments are this project's; no code from usersurf.h or
 * from OpticStudio's sample surfaces is copied here or in us_berreman.cpp. usersurf.h may be used in
 * its place and gives the same layout.
 *
 * The layout is the interface, so it must not be changed: the field order, types and array sizes are
 * what OpticStudio writes.
 *
 * Ansys, OpticStudio and Zemax are trademarks of Ansys, Inc. This example is not made or endorsed by
 * Ansys.
 */

#ifndef UDS_ABI_H
#define UDS_ABI_H

/* USER_DATA: one ray. In order, in memory:
 *   the position x, y, z and the direction cosines l, m, n, which a trace replaces;
 *   the normal at the intercept ln, mn, nn, and the path length to it, which a trace returns;
 *   the sag sag1 and the other root sag2, which a sag request returns;
 *   index and dndx, dndy, dndz, for a gradient-index surface, and rel_surf_tran;
 *   four reserved doubles; then string, 20 characters, for the names the DLL returns. */
typedef struct
{
    double x, y, z, l, m, n;
    double ln, mn, nn, path;
    double sag1, sag2;
    double index, dndx, dndy, dndz, rel_surf_tran;
    double udreserved1, udreserved2, udreserved3, udreserved4;
    char string[20];
} USER_DATA;

/* FIXED_DATA5: one surface. In order, in memory:
 *   ints: type and numb (the request; see us_berreman.cpp), surf, wave, unit, serial, the media
 *     flags is_a_mirror, is_in_mirror_space and is_air, 100 reserved, did_polar (2 when the DLL
 *     sets the electric field itself), max_parameter and max_extradata;
 *   doubles: the ray's electric field Ex, Ey, Ez, Ew as real and imaginary pairs (in on the call,
 *     out on return), 100 reserved, the wavelength and primary wavelength (micrometres), the
 *     indices n1 and n2 before and after the surface, its curvature cv, thickness thic,
 *     semi-diameter sdia and conic k, and the 256 parameter columns;
 *   the glass name and the comment, 200 characters each;
 *   the data-surface values: their count, and up to 4801 of them. */
typedef struct
{
    int type, numb, surf, wave, unit, serial;
    int is_a_mirror, is_in_mirror_space, is_air;
    int ireserved[100];
    int did_polar, max_parameter, max_extradata;
    double Exr, Exi, Eyr, Eyi, Ezr, Ezi, Ewr, Ewi;
    double dbreserved[100];
    double wavelength, pwavelength, n1, n2;
    double cv, thic, sdia, k;
    double param[256];
    char glass[200], comment[200];
    int datasurf_numvalues;
    double datasurf_values[4801];
} FIXED_DATA5;

#endif /* UDS_ABI_H */
