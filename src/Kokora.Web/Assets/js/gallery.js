// Visionneuse de photos (GLightbox), chargée uniquement sur les pages qui ont une galerie.
import GLightbox from 'glightbox';
import 'glightbox/dist/css/glightbox.min.css';

GLightbox({ selector: '.glightbox', touchNavigation: true, loop: false, zoomable: true, descPosition: 'bottom' });
