/* uds_abi.h — the two structures OpticStudio passes to a user-defined surface DLL, as this example
 * uses them.
 *
 * OpticStudio's documentation ("User Defined" surface, UserDefinedSurface5) describes the interface:
 * the function receives a USER_DATA (the ray: position, direction, normal, path) and a FIXED_DATA5
 * (the surface: what is being asked for, the wavelength, the indices either side, the parameters,
 * and the ray's electric field for polarization ray tracing). This header declares the same memory
 * layout so the example builds on its own; OpticStudio's own usersurf.h, in its DLL\Surfaces folder,
 * may be used instead and gives the same layout.
 *
 * The layout is the interface, so it must not be changed: the field order, types and array sizes are
 * what OpticStudio writes.
 */

#ifndef UDS_ABI_H
#define UDS_ABI_H

typedef struct
{
    double x, y, z;                 /* the ray position */
    double l, m, n;                 /* its direction cosines */
    double ln, mn, nn;              /* the surface normal at the intercept, returned */
    double path;                    /* the path to the intercept, returned */
    double sag1, sag2;              /* the sag and the alternate (hyper-hemispheric) sag */
    double index, dndx, dndy, dndz; /* gradient index only */
    double rel_surf_tran;           /* relative surface transmission */
    double udreserved1, udreserved2, udreserved3, udreserved4;
    char string[20];                /* names returned to OpticStudio */
} USER_DATA;

typedef struct
{
    int type, numb;                 /* what is asked for (see us_berreman.cpp) */
    int surf, wave;                 /* the surface number and wavelength number */
    int unit, serial;
    int is_a_mirror, is_in_mirror_space, is_air;
    int ireserved[100];
    int did_polar;                  /* set to 2 when the DLL gives the electric field itself */
    int max_parameter, max_extradata;
    double Exr, Exi, Eyr, Eyi, Ezr, Ezi, Ewr, Ewi; /* the ray's electric field, in and out */
    double dbreserved[100];
    double wavelength, pwavelength; /* in micrometres */
    double n1, n2;                  /* the indices before and after the surface */
    double cv, thic, sdia, k;       /* curvature, thickness, semi-diameter, conic */
    double param[256];              /* the parameter columns */
    char glass[200];
    char comment[200];
    int datasurf_numvalues;
    double datasurf_values[4801];
} FIXED_DATA5;

#endif /* UDS_ABI_H */
