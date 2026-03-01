# SoundBox

Node-based virtual audio processor for Windows.

## Acknowledgments

This project uses the following open-source software:

### FFTW3 — Fastest Fourier Transform in the West

- **Authors**: Matteo Frigo, Steven G. Johnson (Massachusetts Institute of Technology)
- **License**: [GNU General Public License v2](http://www.gnu.org/licenses/old-licenses/gpl-2.0.html)
- **Website**: [https://www.fftw.org/](https://www.fftw.org/)

> FFTW is a C subroutine library for computing the discrete Fourier transform (DFT).
> SoundBox uses FFTW3 for real-time pitch detection and correction in the Auto-Tune and Pitch Shift nodes.
>
> Copyright (c) 2003, 2007-2014 Matteo Frigo
> Copyright (c) 2003, 2007-2014 Massachusetts Institute of Technology

### NAudio — .NET Audio Library

- **Author**: Mark Heath
- **License**: [MIT License](https://opensource.org/licenses/MIT)
- **Repository**: [https://github.com/naudio/NAudio](https://github.com/naudio/NAudio)

> NAudio is an open-source .NET audio library.
> SoundBox uses NAudio (v2.2.1) for WASAPI audio capture and playback.
>
> Copyright (c) Mark Heath

## License

This project is licensed under the GNU General Public License v2. See [LICENSE](LICENSE) for details.
