import {
  BarController,
  BarElement,
  CategoryScale,
  Chart,
  Legend,
  LinearScale,
  Tooltip,
} from 'chart.js';
import React, { useEffect, useRef } from 'react';
import useTheme from 'Helpers/Hooks/useTheme';
import themes from 'Styles/Themes';
import formatBytes from 'Utilities/Number/formatBytes';
import styles from './Statistics.css';

// Only what a stacked bar chart needs, so the rest of the library stays out of the bundle.
Chart.register(
  BarController,
  BarElement,
  CategoryScale,
  LinearScale,
  Legend,
  Tooltip
);

export interface ChartSeries {
  key: string;
  label: string;
  // A theme variable, such as chartColor1.
  color: string;
  values: number[];
}

interface BytesChartProps {
  title: string;
  labels: string[];
  series: ChartSeries[];
  // The same top for every chart on the page, so their bars compare.
  max: number;
}

// A step between gridlines that reads as a round number of KiB, MiB or GiB, so about four of
// them reach the top.
const STEPS = [1, 2, 5, 10, 20, 50, 100, 200, 500];

export function niceStep(max: number) {
  if (max <= 0) {
    return undefined;
  }

  const raw = max / 4;
  const unit = Math.pow(
    1024,
    Math.max(0, Math.floor(Math.log(raw) / Math.log(1024)))
  );
  const step = STEPS.find((s) => s * unit >= raw) ?? 1024;

  return step * unit;
}

function themeColor(theme: keyof typeof themes, name: string): string {
  return (themes[theme] as unknown as Record<string, string>)[name] ?? '';
}

// One bar per day or month, stacked by friend or by app. The colors are the theme's own
// variables, read again whenever the theme changes.
function BytesChart({ title, labels, series, max }: BytesChartProps) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const theme = useTheme();

  useEffect(() => {
    if (!canvas.current) {
      return undefined;
    }

    const color = (name: string) => themeColor(theme, name);
    const surface = color('pageBackground');
    const text = color('textColor');
    const muted = color('helpTextColor');
    const grid = color('borderColor');

    const chart = new Chart(canvas.current, {
      type: 'bar',
      data: {
        labels,
        datasets: series.map((s) => ({
          label: s.label,
          data: s.values,
          backgroundColor: color(s.color),
          hoverBackgroundColor: color(s.color),
          // A gap of the page's own color between stacked segments and between bars.
          borderColor: surface,
          borderWidth: { top: 2, right: 1, bottom: 0, left: 1 },
          borderRadius: 2,
          borderSkipped: 'bottom',
          maxBarThickness: 40,
        })),
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        animation: false,
        interaction: { mode: 'index', intersect: false },
        scales: {
          x: {
            stacked: true,
            grid: { display: false },
            border: { color: grid },
            ticks: { color: muted, maxRotation: 0, autoSkip: true },
          },
          y: {
            stacked: true,
            beginAtZero: true,
            suggestedMax: max > 0 ? max : undefined,
            grid: { color: `${grid}33` },
            border: { display: false },
            ticks: {
              color: muted,
              stepSize: niceStep(max),
              callback: (value) => formatBytes(Number(value)),
            },
          },
        },
        plugins: {
          // Shown for one series too: the title says the direction, the legend says who.
          legend: {
            display: series.length > 0,
            position: 'bottom',
            labels: { color: text, boxWidth: 12, boxHeight: 12 },
          },
          tooltip: {
            filter: (item) => Number(item.raw) > 0,
            callbacks: {
              label: (item) =>
                `${item.dataset.label}: ${formatBytes(Number(item.raw))}`,
            },
          },
        },
      },
    });

    return () => chart.destroy();
  }, [theme, labels, series, max]);

  return (
    <figure className={styles.chart}>
      <figcaption className={styles.chartTitle}>{title}</figcaption>
      <div className={styles.chartCanvas}>
        <canvas ref={canvas} role="img" aria-label={title} />
      </div>
    </figure>
  );
}

export default BytesChart;
