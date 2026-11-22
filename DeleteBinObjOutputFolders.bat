@ECHO OFF
ECHO +=======================+
ECHO += Author: Tarek Najem =+
ECHO += Date:   26-07-2024  =+
ECHO +=======================+
@Echo off
ECHO +===============================================+
ECHO += BIN and OBJ folder cleaning process started =+
ECHO +===============================================+
ECHO.

FOR /F "tokens=*" %%G IN ('DIR /B /AD /S bin') DO (
    IF EXIST "%%G" (
        ECHO Deleting: "%%G"
        RMDIR /S /Q "%%G"
    ) ELSE (
        ECHO "Skipping deletion [Folder not found]:" "%%G": 
    )
)

FOR /F "tokens=*" %%G IN ('DIR /B /AD /S obj') DO (
    IF EXIST "%%G" (
        ECHO Deleting: "%%G"
        RMDIR /S /Q "%%G"
    ) ELSE (
        ECHO "Skipping deletion [Folder not found]:" "%%G": 
    )
)

FOR /F "tokens=*" %%G IN ('DIR /B /AD /S output') DO (
    IF EXIST "%%G" (
        ECHO Deleting: "%%G"
        RMDIR /S /Q "%%G"
    ) ELSE (
        ECHO "Skipping deletion [Folder not found]:" "%%G": 
    )
)
pause