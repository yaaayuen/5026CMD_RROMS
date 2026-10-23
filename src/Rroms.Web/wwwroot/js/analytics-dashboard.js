// Analytics dashboard charts (WL-5).
// Reads the numbers from /Admin/Analytics?handler=Data for the range on the page and draws them with Chart.js.
// The page's tables stay as the accessible fallback, so if this script fails nothing is lost.
(function () {
    "use strict";

    const container = document.getElementById("dashboard-charts");
    if (!container || typeof Chart === "undefined") {
        return;
    }

    // Brand colours from site.css, one per status, so each status always has the same colour.
    const statusColours = {
        "Completed": "#12867a",
        "Seated": "#d81159",
        "Confirmed": "#ffbc42",
        "Pending": "#ff8c42",
        "Cancelled": "#9a9cb0",
        "Absent": "#5c5f77"
    };

    function showError() {
        const error = document.getElementById("charts-error");
        if (error) {
            error.classList.remove("d-none");
        }
    }

    function drawBookings(data) {
        const datasets = data.bookingsByStatus.map(function (series) {
            return {
                type: "bar",
                label: series.name,
                data: series.values,
                backgroundColor: statusColours[series.name] || "#5c5f77",
                stack: "bookings",
                yAxisID: "y",
                order: 2
            };
        });

        datasets.push({
            type: "line",
            label: "Guests served",
            data: data.guestsServed,
            borderColor: "#2b2d42",
            backgroundColor: "#2b2d42",
            tension: 0.25,
            pointRadius: 2,
            yAxisID: "guests",
            // Chart.js draws a lower "order" last, so the guests line sits in front of the bars.
            order: 1
        });

        new Chart(document.getElementById("bookings-chart"), {
            data: { labels: data.dayLabels, datasets: datasets },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: { stacked: true, ticks: { autoSkip: true, maxRotation: 0 } },
                    y: { stacked: true, beginAtZero: true, ticks: { precision: 0 }, title: { display: true, text: "Bookings" } },
                    guests: { position: "right", beginAtZero: true, ticks: { precision: 0 }, grid: { drawOnChartArea: false }, title: { display: true, text: "Guests" } }
                },
                plugins: { legend: { position: "bottom" } }
            }
        });
    }

    function drawHours(data) {
        new Chart(document.getElementById("hours-chart"), {
            type: "bar",
            data: {
                labels: data.hourLabels,
                datasets: [{
                    label: "Table-hours used",
                    data: data.tableHoursByHour,
                    backgroundColor: "#d81159"
                }]
            },
            options: {
                maintainAspectRatio: false,
                scales: { y: { beginAtZero: true, title: { display: true, text: "Table-hours" } } },
                plugins: { legend: { display: false } }
            }
        });
    }

    // Paid sales per day from Darren's DA-3 report, on the same day labels as the bookings chart.
    function drawSales(data) {
        const canvas = document.getElementById("sales-chart");
        if (!canvas) {
            return;
        }

        new Chart(canvas, {
            type: "line",
            data: {
                labels: data.dayLabels,
                datasets: [{
                    label: "Paid sales (RM)",
                    data: data.dailySales,
                    borderColor: "#12867a",
                    backgroundColor: "rgba(18, 134, 122, 0.15)",
                    fill: true,
                    tension: 0.25,
                    pointRadius: 2
                }]
            },
            options: {
                maintainAspectRatio: false,
                interaction: { mode: "index", intersect: false },
                scales: {
                    x: { ticks: { autoSkip: true, maxRotation: 0 } },
                    y: { beginAtZero: true, title: { display: true, text: "RM" } }
                },
                plugins: {
                    legend: { display: false },
                    tooltip: { callbacks: { label: function (item) { return "RM " + Number(item.raw).toFixed(2); } } }
                }
            }
        });
    }

    // Best-selling dishes as horizontal bars, most units at the top.
    function drawDishes(data) {
        const canvas = document.getElementById("dishes-chart");
        if (!canvas || data.popularDishNames.length === 0) {
            return;
        }

        new Chart(canvas, {
            type: "bar",
            data: {
                labels: data.popularDishNames,
                datasets: [{
                    label: "Units sold",
                    data: data.popularDishQuantities,
                    backgroundColor: "#ff8c42"
                }]
            },
            options: {
                indexAxis: "y",
                maintainAspectRatio: false,
                scales: { x: { beginAtZero: true, ticks: { precision: 0 } } },
                plugins: { legend: { display: false } }
            }
        });
    }

    fetch(container.dataset.url, { headers: { "Accept": "application/json" } })
        .then(function (response) {
            if (!response.ok) {
                throw new Error("HTTP " + response.status);
            }
            return response.json();
        })
        .then(function (data) {
            drawBookings(data);
            drawHours(data);
            if (data.salesAvailable) {
                drawSales(data);
                drawDishes(data);
            }
        })
        .catch(showError);
})();
